using ETicaret.Api.Data;
using ETicaret.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ETicaret.Api.Models.Dtos;
using Microsoft.EntityFrameworkCore;
using ETicaret.Api.Models.Entities;
using Microsoft.AspNetCore.Identity;


namespace ETicaret.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class AdminController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly OrderService _orderService;

    public AdminController(AppDbContext context, OrderService orderService)
    {
        _context = context;
        _orderService = orderService;
    }

    private int CurrentUserId => int.Parse(User.FindFirst("UserId")!.Value);

    [HttpGet("orders")]
    public async Task<IActionResult> GetAllOrders([FromQuery] string? searchTerm = null)
    {
        var orders = await _orderService.GetAllOrdersAsync(searchTerm);

        // Ham entity dönmek User/Product'ın gereksiz iç alanlarını (e-posta, rol, maliyet vb.)
        // sızdırır. Panelin ihtiyaç duyduğu alanlarla sınırlı projeksiyon:
        var result = orders.Select(o => new
        {
            o.Id,
            o.UserId,
            o.Status,
            o.CreatedAt,
            o.RecipientName,
            o.Phone,
            o.City,
            o.District,
            o.Address,
            User = new { o.User.Id, o.User.Username, o.User.CreatedAt },
            OrderItems = o.OrderItems.Select(i => new
            {
                i.Id,
                i.Status,
                i.Quantity,
                i.UnitPrice,
                Product = new { i.Product.Name, i.Product.ImageUrl }
            })
        });

        return Ok(result);
    }

    [HttpPut("orders/{id}/approve")]
    public async Task<IActionResult> ApproveOrder(int id)
    {
        try
        {
            await _orderService.ApproveOrderAsync(id, CurrentUserId);
            return Ok(new { message = "Sipariş onaylandı." });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("orders/{id}/reject")]
    public async Task<IActionResult> RejectOrder(int id)
    {
        try
        {
            await _orderService.RejectOrderAsync(id, CurrentUserId);
            return Ok(new { message = "Sipariş reddedildi." });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
    // Kalem bazlı karar: gönderilen kalemler onaylanır, kalanlar reddedilir
    [HttpPut("orders/{id}/decide")]
    public async Task<IActionResult> DecideOrder(int id, OrderDecisionDto dto)
    {
        try
        {
            await _orderService.DecideOrderAsync(id, dto.ApprovedItemIds, CurrentUserId);
            return Ok(new { message = "Sipariş kararı uygulandı." });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("orders/{id}")]
    public async Task<IActionResult> DeleteOrder(int id)
    {
        try
        {
            await _orderService.DeleteOrderAsync(id);
            return Ok(new { message = "Sipariş silindi." });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // Kargoda / Teslim Edildi gibi ileri durumlar için genel güncelleme
    [HttpPut("orders/{id}/status")]
    public async Task<IActionResult> UpdateOrderStatus(int id, OrderStatusDto dto)
    {
        try
        {
            await _orderService.UpdateStatusAsync(id, dto.Status, CurrentUserId);
            return Ok(new { message = "Sipariş durumu güncellendi." });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("users")]
    public async Task<IActionResult> CreateUser(UserUpsertDto dto)
    {
        // Kayıt formuyla aynı kurallar (AuthService içinde tek yerde tanımlı)
        var hata = AuthService.ValidateCredentials(dto.Username, dto.Password, dto.Email);
        if (hata != null)
        {
            return BadRequest(new { message = hata });
        }

        // E-posta burada da zorunlu: panelden e-postasız müşteri açmak, o müşterinin
        // hiçbir sipariş bildirimi alamaması demek — sessiz bir arıza olurdu.
        var epostaHatasi = AuthService.ValidateEmail(dto.Email, required: true);
        if (epostaHatasi != null)
        {
            return BadRequest(new { message = epostaHatasi });
        }

        dto.Username = dto.Username!.Trim();

        if (await _context.Users.AnyAsync(u => u.Username == dto.Username))
        {
            return BadRequest(new { message = "Bu kullanıcı adı zaten kullanılıyor." });
        }

        if (!string.IsNullOrWhiteSpace(dto.Email) && await _context.Users.AnyAsync(u => u.Email == dto.Email))
        {
            return BadRequest(new { message = "Bu e-posta adresi zaten kayıtlı." });
        }

        var role = await _context.Roles.FirstOrDefaultAsync(r => r.Name == (dto.Role ?? "Customer"));
        if (role == null)
        {
            return BadRequest(new { message = "Geçersiz rol." });
        }

        // Yeni kullanıcı listenin sonuna eklensin diye en büyük sıradan bir fazlası verilir
        int nextSortOrder = (await _context.Users.MaxAsync(u => (int?)u.SortOrder) ?? 0) + 1;

        var user = new User
        {
            Username = dto.Username,
            Email = dto.Email,
            Phone = dto.Phone,
            Address = dto.Address,
            RoleId = role.Id,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = User.Identity!.Name,
            SortOrder = nextSortOrder
        };
        // ValidateCredentials yukarıda boş/kısa şifreyi zaten elemiştir (passwordRequired varsayılan true)
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, dto.Password!);

        _context.Users.Add(user);
        await _context.SaveChangesAsync();
        return Ok(new { message = "Kullanıcı eklendi.", user.Id });
    }

    // Boş gelen alanlar değişmez; admin kendi rolünü düşüremez.
    [HttpPut("users/{id}")]
    public async Task<IActionResult> UpdateUser(int id, UserUpsertDto dto)
    {
        var user = await _context.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.Id == id);
        if (user == null)
        {
            return NotFound();
        }

        if (!string.IsNullOrWhiteSpace(dto.Role) && dto.Role != user.Role.Name)
        {
            if (user.Id == CurrentUserId)
            {
                return BadRequest(new { message = "Kendi rolünü değiştiremezsin." });
            }

            var role = await _context.Roles.FirstOrDefaultAsync(r => r.Name == dto.Role);
            if (role == null)
            {
                return BadRequest(new { message = "Geçersiz rol." });
            }
            user.RoleId = role.Id;
        }

        // Güncellemede alanlar tek tek gelebilir; yalnızca gönderilenler doğrulanır.
        // (passwordRequired: false -> şifre gönderilmediyse kurala takılmaz, ama gönderildiyse geçerli olmalı)
        var dogrulamaHatasi = AuthService.ValidateCredentials(
            dto.Username ?? user.Username,
            dto.Password,
            dto.Email ?? user.Email,
            passwordRequired: false);

        if (dogrulamaHatasi != null)
        {
            return BadRequest(new { message = dogrulamaHatasi });
        }

        if (!string.IsNullOrWhiteSpace(dto.Username) && dto.Username.Trim() != user.Username)
        {
            var yeniAd = dto.Username.Trim();
            if (await _context.Users.AnyAsync(u => u.Username == yeniAd && u.Id != id))
            {
                return BadRequest(new { message = "Bu kullanıcı adı zaten kullanılıyor." });
            }
            user.Username = yeniAd;
        }

        if (!string.IsNullOrWhiteSpace(dto.Password))
        {
            user.PasswordHash = new PasswordHasher<User>().HashPassword(user, dto.Password);
        }

        if (dto.Email != null)
        {
            // Boş gelirse null'a çevrilir (telefon/adresle aynı davranış); boş string
            // saklamak "e-postası var ama boş" gibi tutarsız bir duruma yol açardı.
            var yeniEposta = string.IsNullOrWhiteSpace(dto.Email) ? null : dto.Email.Trim();

            if (yeniEposta != null && await _context.Users.AnyAsync(u => u.Email == yeniEposta && u.Id != id))
            {
                return BadRequest(new { message = "Bu e-posta adresi zaten kayıtlı." });
            }
            user.Email = yeniEposta;
        }

        // Boş string gelirse alan temizlenir, null gelirse (formda gönderilmediyse) dokunulmaz
        if (dto.Phone != null)
        {
            user.Phone = string.IsNullOrWhiteSpace(dto.Phone) ? null : dto.Phone.Trim();
        }

        if (dto.Address != null)
        {
            user.Address = string.IsNullOrWhiteSpace(dto.Address) ? null : dto.Address.Trim();
        }

        if (dto.SortOrder != null)
        {
            user.SortOrder = dto.SortOrder.Value;
        }

        if (dto.IsBlocked != null)
        {
            if (dto.IsBlocked.Value && user.Id == CurrentUserId)
            {
                return BadRequest(new { message = "Kendini bloklayamazsın." });
            }

            if (dto.IsBlocked.Value != user.IsBlocked)
            {
                user.IsBlocked = dto.IsBlocked.Value;
                _context.Logs.Add(new Log
                {
                    UserId = CurrentUserId,
                    Action = dto.IsBlocked.Value ? "Kullanıcı bloklandı" : "Kullanıcı bloku kaldırıldı",
                    Details = user.Username,
                    Timestamp = DateTime.UtcNow
                });
            }
        }

        await _context.SaveChangesAsync();
        return Ok(new { message = "Kullanıcı güncellendi." });
    }

    [HttpGet("users")]
public async Task<IActionResult> GetAllUsers()
{
    // Admin'in belirlediği manuel sıraya göre listelenir (aynı sıradaysa Id'ye göre)
    var users = await _context.Users
        .Include(u => u.Role)
        .OrderBy(u => u.SortOrder).ThenBy(u => u.Id)
        .Select(u => new UserSummaryDto
        {
            Id = u.Id,
            Username = u.Username,
            Email = u.Email,
            Phone = u.Phone,
            Address = u.Address,
            Role = u.Role.Name,
            CreatedAt = u.CreatedAt,
            SortOrder = u.SortOrder,
            IsBlocked = u.IsBlocked
        })
        .ToListAsync();

    return Ok(users);
}

[HttpDelete("users/{id}")]
public async Task<IActionResult> DeleteUser(int id)
{
    var user = await _context.Users.FindAsync(id);
    if (user == null)
    {
        return NotFound();
    }

    try
    {
        _context.Users.Remove(user);
        await _context.SaveChangesAsync();
        return NoContent();
    }
    catch (DbUpdateException)
    {
        return BadRequest(new { message = "Bu kullanıcının siparişleri olduğu için silinemiyor." });
    }
}

// Bir ürünün sipariş tarihçesi: bu ürün hangi siparişte, kim tarafından, ne zaman, kaç adet,
// hangi fiyattan alınmış. Analiz sekmesindeki ProductSales toplamının satır satır dökümü.
[HttpGet("products/{id}/orders")]
public async Task<IActionResult> GetProductOrderHistory(int id)
{
    var product = await _context.Products.FindAsync(id);
    if (product == null)
    {
        return NotFound();
    }

    // Varsayılan sıra: en yeni sipariş üstte
    var query = _context.OrderItems
        .Where(oi => oi.ProductId == id)
        .OrderByDescending(oi => oi.Order.CreatedAt);

    return await BuildProductHistoryResult(query, product);
}

// Ürün geçmişi için generic çoklu-kolon filtre ucu (Task #3). Frontend'in Ürün Geçmişi
// modalındaki sütun filtrelerinden (müşteri adı, tarih aralığı, adet, birim fiyat, durum)
// ürettiği kurallar OrderItem entity'si üzerinde VERİTABANINDA uygulanır.
// Filtre alanları örn.: "Order.User.Username", "Order.CreatedAt", "Quantity", "UnitPrice", "Status".
[HttpPost("products/{id}/orders/filter")]
public async Task<IActionResult> FilterProductOrderHistory(int id, FilterRequest request)
{
    var product = await _context.Products.FindAsync(id);
    if (product == null)
    {
        return NotFound();
    }

    var query = _context.OrderItems
        .Where(oi => oi.ProductId == id)
        .ApplyFilters(request.Filters)
        .ApplySort(request.SortBy, request.SortDir);

    // Kullanıcı sıralama vermediyse yine en yeni üstte kalsın
    if (string.IsNullOrWhiteSpace(request.SortBy))
    {
        query = query.OrderByDescending(oi => oi.Order.CreatedAt);
    }

    return await BuildProductHistoryResult(query, product);
}

// Ürün geçmişi projeksiyonu + özet: hem filtreli hem filtresiz uç bunu paylaşır.
private async Task<IActionResult> BuildProductHistoryResult(IQueryable<OrderItem> query, Product product)
{
    var items = await query
        .Select(oi => new
        {
            oi.OrderId,
            oi.Order.CreatedAt,
            Customer = oi.Order.User.Username,
            CustomerId = oi.Order.UserId,
            oi.Quantity,
            oi.UnitPrice,
            LineTotal = oi.UnitPrice * oi.Quantity,
            ItemStatus = oi.Status,        // Bu kalem onaylandı mı reddedildi mi
            OrderStatus = oi.Order.Status  // Siparişin genel durumu
        })
        .ToListAsync();

    // Özet: sadece onaylanmış kalemler gerçek satıştır
    var sold = items.Where(i => i.ItemStatus == OrderStatus.Approved).ToList();

    return Ok(new
    {
        ProductId = product.Id,
        ProductName = product.Name,
        TotalOrders = items.Select(i => i.OrderId).Distinct().Count(),
        TotalSoldQty = sold.Sum(i => i.Quantity),
        TotalRevenue = sold.Sum(i => i.LineTotal),
        TotalProfit = sold.Sum(i => (i.UnitPrice - (product.Cost ?? 0)) * i.Quantity),
        Items = items
    });
}

// Yeni sipariş bildirimi için hafif uç nokta: panel bunu periyodik yoklar.
// LatestOrderId, en son siparişin id'si — panel bunu son gördüğüyle karşılaştırıp
// yeni sipariş gelip gelmediğini anlar (tüm sipariş listesini tekrar çekmeye gerek kalmaz).
[HttpGet("orders/pending-count")]
public async Task<IActionResult> GetPendingOrderCount()
{
    return Ok(new
    {
        Count = await _context.Orders.CountAsync(o => o.Status == OrderStatus.Pending),
        LatestOrderId = await _context.Orders.MaxAsync(o => (int?)o.Id) ?? 0
    });
}

// Canlı akış (gerçek veri): en son siparişler. afterId verilirse yalnızca ondan YENİ olanlar
// döner (poll'de sadece yeni gelenleri çekmek için); verilmezse son N sipariş (ilk yükleme).
// Hafif tutulur: akışta gösterilecek alanlar (müşteri, il, kalem sayısı, tutar) SQL'de projelenir.
[HttpGet("orders/recent")]
public async Task<IActionResult> GetRecentOrders([FromQuery] int afterId = 0, [FromQuery] int take = 15)
{
    if (take < 1) take = 1;
    if (take > 50) take = 50;   // akış için üst sınır

    var q = _context.Orders.AsQueryable();
    if (afterId > 0)
    {
        q = q.Where(o => o.Id > afterId);
    }

    var orders = await q
        .OrderByDescending(o => o.Id)
        .Take(take)
        .Select(o => new
        {
            o.Id,
            Customer = o.User.Username,
            CustomerId = o.UserId,
            City = o.City ?? "Belirtilmemiş",
            District = o.District,
            o.Status,
            o.CreatedAt,
            // Reddedilen kalemler tutara girmez (panelin geri kalanıyla tutarlı).
            ItemCount = o.OrderItems.Count(i => i.Status != OrderStatus.Rejected),
            Total = o.OrderItems.Where(i => i.Status != OrderStatus.Rejected)
                .Sum(i => (decimal?)(i.UnitPrice * i.Quantity)) ?? 0m
        })
        .ToListAsync();

    // İstemci "yeni gelenleri en üste ekle" mantığı kullandığı için ESKİDEN YENİYE sıralı döneriz.
    orders.Reverse();
    return Ok(orders);
}

// ===== İl / İlçe bazında sipariş istatistikleri (Task #6) =====
//
// On binlerce siparişte tüm kayıtları çekip tarayıcıda gruplamak hem ağı hem arayüzü boğar.
// Burada gruplama DOĞRUDAN SQL'de (GROUP BY) yapılır; sunucudan yalnızca il/ilçe sayısı kadar
// (birkaç yüz satır) hafif özet döner. Ciro, reddedilen kalemler hariç kalemler üzerinden
// ayrı bir GROUP BY ile hesaplanıp bellekte il/ilçe anahtarıyla birleştirilir.

// İl seviyesi: sadece iller + her ilin sipariş adedi, ciro ve ilçe sayısı.
[HttpGet("orders/region-stats")]
public async Task<IActionResult> GetRegionStats()
{
    // Sipariş adetleri il+ilçe kırılımında SQL'de gruplanır (ham sipariş taşınmaz).
    var perDistrict = await _context.Orders
        .GroupBy(o => new { City = o.City ?? "Belirtilmemiş", District = o.District ?? "Belirtilmemiş" })
        .Select(g => new { g.Key.City, g.Key.District, OrderCount = g.Count() })
        .ToListAsync();

    // Ciro, reddedilen kalemler hariç, yine SQL'de il+ilçe bazında toplanır.
    var revPerDistrict = await _context.OrderItems
        .Where(i => i.Status != OrderStatus.Rejected)
        .GroupBy(i => new { City = i.Order.City ?? "Belirtilmemiş", District = i.Order.District ?? "Belirtilmemiş" })
        .Select(g => new { g.Key.City, g.Key.District, Revenue = g.Sum(i => i.UnitPrice * i.Quantity) })
        .ToListAsync();

    var revMap = revPerDistrict.ToDictionary(x => (x.City, x.District), x => x.Revenue);

    // İl seviyesine roll-up: birleştirme yalnızca birkaç yüz satır üzerinde, bellekte ucuz.
    var provinces = perDistrict
        .GroupBy(x => x.City)
        .Select(g => new CityStatDto
        {
            City = g.Key,
            OrderCount = g.Sum(x => x.OrderCount),
            DistrictCount = g.Count(),
            TotalRevenue = g.Sum(x => revMap.GetValueOrDefault((x.City, x.District), 0m))
        })
        .OrderByDescending(x => x.OrderCount)
        .ThenBy(x => x.City)
        .ToList();

    return Ok(provinces);
}

// İlçe seviyesi (drill-down): bir ile tıklanınca yalnızca o ilin ilçeleri yüklenir.
[HttpGet("orders/region-stats/districts")]
public async Task<IActionResult> GetDistrictStats([FromQuery] string city)
{
    if (string.IsNullOrEmpty(city))
    {
        return BadRequest(new { message = "İl parametresi gerekli." });
    }

    var counts = await _context.Orders
        .Where(o => (o.City ?? "Belirtilmemiş") == city)
        .GroupBy(o => o.District ?? "Belirtilmemiş")
        .Select(g => new { District = g.Key, OrderCount = g.Count() })
        .ToListAsync();

    var revenue = await _context.OrderItems
        .Where(i => i.Status != OrderStatus.Rejected && (i.Order.City ?? "Belirtilmemiş") == city)
        .GroupBy(i => i.Order.District ?? "Belirtilmemiş")
        .Select(g => new { District = g.Key, Revenue = g.Sum(i => i.UnitPrice * i.Quantity) })
        .ToListAsync();

    var revMap = revenue.ToDictionary(x => x.District, x => x.Revenue);

    var districts = counts
        .Select(c => new CityDistrictStatDto
        {
            City = city,
            District = c.District,
            OrderCount = c.OrderCount,
            TotalRevenue = revMap.GetValueOrDefault(c.District, 0m)
        })
        .OrderByDescending(x => x.OrderCount)
        .ThenBy(x => x.District)
        .ToList();

    return Ok(districts);
}

[HttpGet("logs")]
public async Task<IActionResult> GetLogs()
{
    var logs = await _context.Logs
        .OrderByDescending(l => l.Timestamp)
        .Take(200)
        .Select(l => new
        {
            l.Id,
            l.UserId,
            // İsteğin anındaki ad öncelikli; yoksa (eski kayıtlar) ilişkiden okunur.
            Username = l.Username ?? (l.User != null ? l.User.Username : null),
            l.Action,
            l.Details,
            l.HttpMethod,
            l.Path,
            l.StatusCode,
            l.IpAddress,
            l.UserAgent,
            l.DurationMs,
            l.RequestBody,
            l.ResponseBody,
            l.Exception,
            l.StackTrace,
            l.Timestamp
        })
        .ToListAsync();

    return Ok(logs);
}

// from/to verilirse (tarih, saatsiz — örn. "2026-07-01"), bu serbest aralık için ayrı bir
// gelir/kâr/ürün-satış dökümü de hesaplanıp "Range" alanında döner (sabit Bugün/Hafta/Ay/Yıl kartları
// bundan etkilenmez). "to" günü de dahil olsun diye gün sonuna (23:59:59.999) tamamlanır.
// from/to (tarih aralığı), categoryId (kategori) ve status (sipariş durumu) ile çok yönlü
// filtrelenebilir. categoryId satış metriklerini (gelir/kâr/ürün satışı/trend/en iyi müşteri)
// o kategoriye (ve alt kategorilerine) daraltır; status ise "Filtreli Siparişler" tablosunu süzer.
[HttpGet("analytics")]
public async Task<IActionResult> GetAnalytics(
    [FromQuery] DateTime? from,
    [FromQuery] DateTime? to,
    [FromQuery] int? categoryId,
    [FromQuery] OrderStatus? status,
    [FromQuery] string? customer,
    [FromQuery] decimal? minTotal,
    [FromQuery] decimal? maxTotal,
    [FromQuery] string? city)
{
    // Gerçek satış = onaylanmış kalemler (sipariş kargoya/teslime geçse de kalem "Approved" kalır)
    var soldItems = await _context.OrderItems
        .Where(oi => oi.Status == OrderStatus.Approved)
        .Include(oi => oi.Product)
            .ThenInclude(p => p.Category)
        .Include(oi => oi.Order)
            .ThenInclude(o => o.User)
        .ToListAsync();

    // Kategori filtresi: seçilen kategori VEYA onun alt kategorilerindeki ürünler.
    // (Ana kategori seçilince altındaki ürünler de analizde görünsün.)
    if (categoryId != null)
    {
        soldItems = soldItems.Where(i => i.Product.CategoryId == categoryId
            || (i.Product.Category != null && i.Product.Category.ParentId == categoryId)).ToList();
    }

    decimal totalRevenue = soldItems.Sum(i => i.UnitPrice * i.Quantity);
    decimal totalCost = soldItems.Sum(i => (i.Product.Cost ?? 0) * i.Quantity);
    decimal totalProfit = totalRevenue - totalCost;

    var bestSelling = soldItems
        .GroupBy(i => i.Product.Name)
        .Select(g => new { ProductName = g.Key, TotalSold = g.Sum(i => i.Quantity) })
        .OrderByDescending(g => g.TotalSold)
        .FirstOrDefault();

    // Kategori bazında ciro (en çok kazandıran kategoriler)
    var revenueByCategory = soldItems
        .GroupBy(i => i.Product.Category != null ? i.Product.Category.Name : "-")
        .Select(g => new { Category = g.Key, Revenue = g.Sum(i => i.UnitPrice * i.Quantity) })
        .OrderByDescending(x => x.Revenue)
        .ToList();

    // Ürün bazında satış geçmişi: kaç satıldı, ciro, kâr
    var productSales = soldItems
        .GroupBy(i => i.Product.Name)
        .Select(g => new
        {
            Product = g.Key,
            QtySold = g.Sum(i => i.Quantity),
            Revenue = g.Sum(i => i.UnitPrice * i.Quantity),
            Profit = g.Sum(i => (i.UnitPrice - (i.Product.Cost ?? 0)) * i.Quantity)
        })
        .OrderByDescending(x => x.QtySold)
        .ToList();

    // Zaman bazlı gelir/kâr (siparişin oluşturulma tarihine göre)
    var now = DateTime.UtcNow;
    object Period(DateTime from)
    {
        var items = soldItems.Where(i => i.Order.CreatedAt >= from).ToList();
        var rev = items.Sum(i => i.UnitPrice * i.Quantity);
        var cost = items.Sum(i => (i.Product.Cost ?? 0) * i.Quantity);
        return new { Revenue = rev, Profit = rev - cost };
    }

    // Serbest tarih aralığı: admin takvimden istediği günü/haftayı/ayı seçip
    // o aralıktaki gelir/kâr/ürün bazlı satışı (kaç adet) görebilir
    object? range = null;
    if (from != null || to != null)
    {
        var rangeStart = (from ?? DateTime.MinValue).Date;
        var rangeEnd = (to ?? now).Date.AddDays(1).AddTicks(-1); // seçilen bitiş günü de dahil olsun

        var rangeItems = soldItems.Where(i => i.Order.CreatedAt >= rangeStart && i.Order.CreatedAt <= rangeEnd).ToList();
        var rangeRevenue = rangeItems.Sum(i => i.UnitPrice * i.Quantity);
        var rangeCost = rangeItems.Sum(i => (i.Product.Cost ?? 0) * i.Quantity);

        var rangeProductSales = rangeItems
            .GroupBy(i => i.Product.Name)
            .Select(g => new
            {
                Product = g.Key,
                QtySold = g.Sum(i => i.Quantity),
                Revenue = g.Sum(i => i.UnitPrice * i.Quantity),
                Profit = g.Sum(i => (i.UnitPrice - (i.Product.Cost ?? 0)) * i.Quantity)
            })
            .OrderByDescending(x => x.QtySold)
            .ToList();

        range = new
        {
            From = rangeStart,
            To = rangeEnd,
            Revenue = rangeRevenue,
            Cost = rangeCost,
            Profit = rangeRevenue - rangeCost,
            OrderCount = rangeItems.Select(i => i.OrderId).Distinct().Count(),
            ProductSales = rangeProductSales
        };
    }

    // ===== Günlük trend (çizgi/alan grafiği için) =====
    // from/to verilmediyse son 30 gün. Her gün için gelir/kâr/sipariş adedi.
    var trendStart = (from ?? now.Date.AddDays(-29)).Date;
    var trendEnd = (to ?? now).Date.AddDays(1).AddTicks(-1);
    var trend = soldItems
        .Where(i => i.Order.CreatedAt >= trendStart && i.Order.CreatedAt <= trendEnd)
        .GroupBy(i => i.Order.CreatedAt.Date)
        .Select(g => new
        {
            Date = g.Key,
            Revenue = g.Sum(i => i.UnitPrice * i.Quantity),
            Profit = g.Sum(i => (i.UnitPrice - (i.Product.Cost ?? 0)) * i.Quantity),
            Orders = g.Select(i => i.OrderId).Distinct().Count()
        })
        .OrderBy(x => x.Date)
        .ToList();

    // ===== En çok harcayan müşteriler (tablo) =====
    var topCustomers = soldItems
        .GroupBy(i => new { i.Order.UserId, i.Order.User.Username })
        .Select(g => new
        {
            g.Key.UserId,
            Customer = g.Key.Username,
            Orders = g.Select(i => i.OrderId).Distinct().Count(),
            Spent = g.Sum(i => i.UnitPrice * i.Quantity)
        })
        .OrderByDescending(x => x.Spent)
        .Take(5)
        .ToList();

    // ===== Ek özet metrikler (KPI kartları) =====
    int totalUnitsSold = soldItems.Sum(i => i.Quantity);
    int distinctProductsSold = soldItems.Select(i => i.ProductId).Distinct().Count();
    int distinctSoldOrders = soldItems.Select(i => i.OrderId).Distinct().Count();
    decimal averageOrderValue = distinctSoldOrders > 0 ? Math.Round(totalRevenue / distinctSoldOrders, 2) : 0m;
    decimal profitMargin = totalRevenue > 0 ? Math.Round(totalProfit / totalRevenue * 100m, 1) : 0m;

    // ===== Kategori kırılımı (adet + ciro + kâr) — bar grafiğin tablo karşılığı =====
    var categoryBreakdown = soldItems
        .GroupBy(i => i.Product.Category != null ? i.Product.Category.Name : "-")
        .Select(g => new
        {
            Category = g.Key,
            QtySold = g.Sum(i => i.Quantity),
            Revenue = g.Sum(i => i.UnitPrice * i.Quantity),
            Profit = g.Sum(i => (i.UnitPrice - (i.Product.Cost ?? 0)) * i.Quantity)
        })
        .OrderByDescending(x => x.Revenue)
        .ToList();

    // ===== En kârlı ürünler (adet değil, kâr sıralaması) =====
    var topProfitProducts = productSales
        .OrderByDescending(x => x.Profit)
        .Take(5)
        .ToList();

    // ===== Aylık kırılım (son 12 ay): ciro / kâr / sipariş =====
    var monthWindowStart = new DateTime(now.Year, now.Month, 1).AddMonths(-11);
    var monthlyBreakdown = soldItems
        .Where(i => i.Order.CreatedAt >= monthWindowStart)
        .GroupBy(i => new { i.Order.CreatedAt.Year, i.Order.CreatedAt.Month })
        .Select(g => new
        {
            Label = $"{g.Key.Month:00}.{g.Key.Year}",
            Revenue = g.Sum(i => i.UnitPrice * i.Quantity),
            Profit = g.Sum(i => (i.UnitPrice - (i.Product.Cost ?? 0)) * i.Quantity),
            Orders = g.Select(i => i.OrderId).Distinct().Count(),
            g.Key.Year,
            g.Key.Month
        })
        .OrderByDescending(x => x.Year).ThenByDescending(x => x.Month)
        .ToList();

    // ===== Kupon kullanımı: kupon kodu bazında kaç sipariş, ne kadar indirim =====
    var couponOrders = await _context.Orders
        .Where(o => o.CouponCode != null)
        .Include(o => o.OrderItems)
        .ToListAsync();
    var couponUsage = couponOrders
        .GroupBy(o => o.CouponCode!)
        .Select(g => new
        {
            Code = g.Key,
            Count = g.Count(),
            TotalDiscount = g.Sum(o => o.CalculateDiscount(
                o.OrderItems.Where(i => i.Status != OrderStatus.Rejected)
                            .Sum(i => i.UnitPrice * i.Quantity)))
        })
        .OrderByDescending(x => x.TotalDiscount)
        .ToList();

    // ===== Envanter değeri + düşük stok listesi (operasyonel) =====
    var activeStock = await _context.Products
        .Where(p => p.IsActive)
        .Select(p => new { p.Stock, p.Cost, p.Price })
        .ToListAsync();
    decimal inventoryCost = activeStock.Sum(p => (p.Cost ?? 0) * p.Stock);
    decimal inventoryRetail = activeStock.Sum(p => p.Price * p.Stock);

    var lowStockList = await _context.Products
        .Where(p => p.IsActive && p.Stock <= 5)
        .OrderBy(p => p.Stock)
        .Select(p => new
        {
            p.Name,
            p.Stock,
            Category = p.Category != null ? p.Category.Name : "-"
        })
        .Take(20)
        .ToListAsync();

    // ===== Müşteri metrikleri =====
    var monthStartCur = new DateTime(now.Year, now.Month, 1);
    int newCustomersThisMonth = await _context.Users
        .CountAsync(u => u.Role.Name == "Customer" && u.CreatedAt >= monthStartCur);
    // Birden fazla ONAYLANMIŞ siparişi olan (sadık) müşteri sayısı
    int repeatCustomers = soldItems
        .GroupBy(i => i.Order.UserId)
        .Count(g => g.Select(i => i.OrderId).Distinct().Count() > 1);

    // ===== Filtreli siparişler tablosu (tarih + durum + kategori) =====
    // "Detaylı analiz tablosu": seçilen filtrelere uyan siparişler tek tek listelenir.
    // Herhangi bir filtre yoksa null döner (varsayılan ekranı gereksiz kalabalıklaştırmamak için).
    object? filteredOrders = null;
    // customer/minTotal/maxTotal/city de "Filtreli Siparişler" tablosunu süzer (status gibi).
    bool hasCustomer = !string.IsNullOrWhiteSpace(customer);
    bool hasCity = !string.IsNullOrWhiteSpace(city);
    bool hasFilter = from != null || to != null || status != null || categoryId != null
        || hasCustomer || minTotal != null || maxTotal != null || hasCity;
    if (hasFilter)
    {
        var fStart = (from ?? DateTime.MinValue).Date;
        var fEnd = (to ?? now).Date.AddDays(1).AddTicks(-1);

        var oq = _context.Orders
            .Where(o => o.CreatedAt >= fStart && o.CreatedAt <= fEnd);

        if (status != null)
        {
            oq = oq.Where(o => o.Status == status);
        }
        if (categoryId != null)
        {
            oq = oq.Where(o => o.OrderItems.Any(oi =>
                oi.Product.CategoryId == categoryId || oi.Product.Category.ParentId == categoryId));
        }
        // Müşteri: adı içinde arama VEYA doğrudan müşteri numarası ("#12" ya da "12").
        if (hasCustomer)
        {
            var term = customer!.Trim().TrimStart('#');
            oq = int.TryParse(term, out var custId)
                ? oq.Where(o => o.UserId == custId || o.User.Username.Contains(term))
                : oq.Where(o => o.User.Username.Contains(term));
        }
        // Şehir: serbest metin, kısmi eşleşme.
        if (hasCity)
        {
            var c = city!.Trim();
            oq = oq.Where(o => o.City != null && o.City.Contains(c));
        }
        // Tutar aralığı: reddedilmeyen kalemlerin toplamına göre (tablodaki "Tutar" ile aynı hesap).
        if (minTotal != null)
        {
            oq = oq.Where(o => o.OrderItems.Where(i => i.Status != OrderStatus.Rejected)
                .Sum(i => (decimal?)(i.UnitPrice * i.Quantity)) >= minTotal);
        }
        if (maxTotal != null)
        {
            oq = oq.Where(o => o.OrderItems.Where(i => i.Status != OrderStatus.Rejected)
                .Sum(i => (decimal?)(i.UnitPrice * i.Quantity)) <= maxTotal);
        }

        filteredOrders = await oq
            .OrderByDescending(o => o.CreatedAt)
            .Take(100)
            .Select(o => new
            {
                o.Id,
                o.CreatedAt,
                o.Status,
                Customer = o.User.Username,
                CustomerId = o.UserId,
                City = o.City,
                ItemCount = o.OrderItems.Count(i => i.Status != OrderStatus.Rejected),
                Total = o.OrderItems.Where(i => i.Status != OrderStatus.Rejected)
                    .Sum(i => i.UnitPrice * i.Quantity)
            })
            .ToListAsync();
    }

    var analytics = new
    {
        Daily = Period(now.Date),
        Weekly = Period(now.Date.AddDays(-6)),
        Monthly = Period(new DateTime(now.Year, now.Month, 1)),
        Yearly = Period(new DateTime(now.Year, 1, 1)),
        Range = range,
        ProductSales = productSales,

        TotalOrders = await _context.Orders.CountAsync(),
        PendingOrders = await _context.Orders.CountAsync(o => o.Status == OrderStatus.Pending),
        ApprovedOrders = await _context.Orders.CountAsync(o => o.Status == OrderStatus.Approved),
        RejectedOrders = await _context.Orders.CountAsync(o => o.Status == OrderStatus.Rejected),
        ShippedOrders = await _context.Orders.CountAsync(o => o.Status == OrderStatus.Shipped),
        DeliveredOrders = await _context.Orders.CountAsync(o => o.Status == OrderStatus.Delivered),
        CancelledOrders = await _context.Orders.CountAsync(o => o.Status == OrderStatus.Cancelled),

        TotalRevenue = totalRevenue,
        TotalCost = totalCost,
        TotalProfit = totalProfit,

        BestSellingProduct = bestSelling?.ProductName,
        BestSellingProductQuantity = bestSelling?.TotalSold,

        RevenueByCategory = revenueByCategory,

        ActiveProducts = await _context.Products.CountAsync(p => p.IsActive),
        LowStockProducts = await _context.Products.CountAsync(p => p.IsActive && p.Stock > 0 && p.Stock <= 5),
        OutOfStockProducts = await _context.Products.CountAsync(p => p.IsActive && p.Stock == 0),

        CustomerCount = await _context.Users.CountAsync(u => u.Role.Name == "Customer"),

        // ===== Task #5: zenginleştirilmiş analiz =====
        Trend = trend,                 // günlük gelir/kâr/sipariş (çizgi grafiği)
        TopCustomers = topCustomers,   // en çok harcayan 5 müşteri (tablo)
        FilteredOrders = filteredOrders, // filtrelere uyan siparişler (detay tablo) — filtre yoksa null
        SelectedCategoryId = categoryId,
        SelectedStatus = status,

        // ===== Genişletilmiş analiz metrikleri =====
        TotalUnitsSold = totalUnitsSold,               // toplam satılan adet
        DistinctProductsSold = distinctProductsSold,   // kaç farklı ürün satıldı
        AverageOrderValue = averageOrderValue,         // ortalama sipariş tutarı
        ProfitMargin = profitMargin,                   // kâr marjı (%)
        InventoryCost = inventoryCost,                 // envanterin maliyet değeri
        InventoryRetail = inventoryRetail,             // envanterin satış (perakende) değeri
        NewCustomersThisMonth = newCustomersThisMonth, // bu ay katılan müşteri
        RepeatCustomers = repeatCustomers,             // birden çok siparişi olan müşteri

        CategoryBreakdown = categoryBreakdown,         // kategori: adet/ciro/kâr (tablo)
        TopProfitProducts = topProfitProducts,         // en kârlı 5 ürün (tablo)
        MonthlyBreakdown = monthlyBreakdown,           // son 12 ay: ciro/kâr/sipariş (tablo)
        CouponUsage = couponUsage,                     // kupon kodu bazında kullanım/indirim (tablo)
        LowStockList = lowStockList                    // düşük stoklu ürünler (tablo)
    };

    return Ok(analytics);
}

}
