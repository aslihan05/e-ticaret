using ETicaret.Api.Data;
using ETicaret.Api.Models.Dtos;
using ETicaret.Api.Models.Entities;
using ETicaret.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ETicaret.Api.Controllers;

// "Haftanın Fırsatı" bölümü: adminin bu hafta için seçtiği ürünler, oranları ve geri sayım.
// İndirimler GERÇEK indirimdir — ürünün kendi DiscountPrice/Start/End alanlarına yazılır,
// böylece sepette, ürün sayfasında ve tüm sitede aynı fiyat geçerli olur; sayaç (EndsAt)
// dolunca indirim kendiliğinden düşer (IsDiscountActive tarih kontrolü yapar).
[ApiController]
[Route("api/[controller]")]
public class WeeklyDealsController : ControllerBase
{
    private readonly AppDbContext _context;

    public WeeklyDealsController(AppDbContext context)
    {
        _context = context;
    }

    // Tekil konfig satırını getirir; yoksa (ilk kullanım) varsayılanla oluşturur.
    private async Task<WeeklyDealConfig> GetOrCreateConfigAsync()
    {
        var config = await _context.WeeklyDealConfigs.FirstOrDefaultAsync();
        if (config == null)
        {
            config = new WeeklyDealConfig();
            _context.WeeklyDealConfigs.Add(config);
            await _context.SaveChangesAsync();
        }
        return config;
    }

    // Girilen yüzdeden indirimli fiyatı üretir (2 basamak). İndirim sisteminin başka
    // yerlerindeki (kategori/toplu indirim) hesabıyla birebir aynı formül.
    private static decimal PriceFromPercent(decimal price, int percent) =>
        Math.Round(price * (100 - percent) / 100m, 2);

    // Bir müsait stok hesabı: depo − sepet rezervasyonları − onay bekleyen siparişler.
    // (ProductsController'daki InStock mantığının aynısı; vitrin de tükenmiş ürünü doğru göstersin.)
    private static bool ComputeInStock(AppDbContext ctx, Product p, DateTime rezervasyonSiniri) =>
        p.Stock
        - ctx.CartItems.Where(ci => ci.ProductId == p.Id && ci.AddedAt > rezervasyonSiniri).Sum(ci => ci.Quantity)
        - ctx.OrderItems.Where(oi => oi.ProductId == p.Id
            && oi.Status == OrderStatus.Pending && oi.Order.Status == OrderStatus.Pending).Sum(oi => oi.Quantity)
        > 0;

    private static int PercentOf(decimal price, decimal? discounted) =>
        (discounted != null && price > 0) ? (int)Math.Round((1 - discounted.Value / price) * 100m) : 0;

    // ===== Ziyaretçiye açık: vitrin =====
    // Bölüm yalnızca aktif VE (bitiş yoksa ya da bitiş henüz gelmediyse) döner; süresi
    // geçmişse IsActive=false döner ve frontend bölümü hiç göstermez.
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var now = DateTime.UtcNow;
        var config = await _context.WeeklyDealConfigs.AsNoTracking().FirstOrDefaultAsync();

        // Bölüm; açık olmalı, başlangıcı gelmiş olmalı ve bitişi geçmemiş olmalı. Başlangıç
        // tarihi sayesinde admin kampanyayı önceden hazırlayabilir: gününe kadar vitrinde
        // hiç görünmez, saati gelince kendiliğinden açılır.
        bool aktif = config is { IsActive: true }
                     && (config.StartsAt == null || config.StartsAt <= now)
                     && (config.EndsAt == null || config.EndsAt > now);
        if (!aktif)
        {
            return Ok(new WeeklyDealPublicDto { IsActive = false, Items = new() });
        }

        var rezervasyonSiniri = now - StockService.ReservationWindow;

        var products = await _context.Products
            .Include(p => p.Reviews)
            .Where(p => p.IsActive && p.WeeklyDealOrder != null)
            .OrderBy(p => p.WeeklyDealOrder)
            .ToListAsync();

        var items = products.Select(p =>
        {
            // Vitrinde her zaman planlanan indirimli fiyatı göster (bölüm aktifken indirim de aktiftir)
            var discounted = p.DiscountPrice ?? p.Price;
            return new WeeklyDealItemPublicDto
            {
                Id = p.Id,
                Name = p.Name,
                Description = p.Description,
                ImageUrl = p.ImageUrl,
                Price = p.Price,
                DiscountedPrice = discounted,
                DiscountPercent = PercentOf(p.Price, discounted < p.Price ? discounted : null),
                HasDiscount = discounted < p.Price,
                InStock = ComputeInStock(_context, p, rezervasyonSiniri),
                ReviewCount = p.Reviews.Count,
                AverageRating = p.Reviews.Count > 0 ? Math.Round(p.Reviews.Average(r => r.Rating), 1) : 0
            };
        }).ToList();

        return Ok(new WeeklyDealPublicDto
        {
            Title = config!.Title,
            StartsAt = config.StartsAt,
            EndsAt = config.EndsAt,
            IsActive = true,
            Items = items
        });
    }

    // ===== Admin: tam görünüm =====
    [HttpGet("admin")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetAdmin()
    {
        var config = await GetOrCreateConfigAsync();

        var products = await _context.Products
            .Include(p => p.Category)
            .Where(p => p.WeeklyDealOrder != null)
            .OrderBy(p => p.WeeklyDealOrder)
            .ToListAsync();

        var items = products.Select(p => new WeeklyDealItemAdminDto
        {
            ProductId = p.Id,
            Name = p.Name,
            ImageUrl = p.ImageUrl,
            Price = p.Price,
            DiscountedPrice = p.DiscountPrice,
            DiscountPercent = PercentOf(p.Price, p.DiscountPrice),
            SortOrder = p.WeeklyDealOrder ?? 0,
            CategoryName = p.Category != null ? p.Category.Name : null,
            Stock = p.Stock
        }).ToList();

        return Ok(new WeeklyDealAdminDto
        {
            Title = config.Title,
            StartsAt = config.StartsAt,
            EndsAt = config.EndsAt,
            IsActive = config.IsActive,
            Items = items
        });
    }

    // ===== Admin: ayarları güncelle =====
    // Başlangıç/bitiş tarihi değişince vitrindeki TÜM ürünlerin DiscountStart/DiscountEnd'i
    // bunlara eşitlenir; böylece sayaç ile gerçek indirimin aralığı hep aynı anı gösterir:
    // kampanya başlamadan indirim uygulanmaz, sayaç bitince indirim kendiliğinden düşer.
    [HttpPut("admin/settings")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> UpdateSettings(WeeklyDealSettingsDto dto)
    {
        if (dto.StartsAt != null && dto.EndsAt != null && dto.StartsAt >= dto.EndsAt)
        {
            return BadRequest(new { message = "Başlangıç tarihi bitiş tarihinden önce olmalı." });
        }

        var config = await GetOrCreateConfigAsync();

        if (!string.IsNullOrWhiteSpace(dto.Title))
        {
            config.Title = dto.Title.Trim();
        }
        config.StartsAt = dto.StartsAt;
        config.EndsAt = dto.EndsAt;
        config.IsActive = dto.IsActive;

        // Tarihleri vitrindeki ürünlerin indirim aralığına yansıt
        var dealProducts = await _context.Products.Where(p => p.WeeklyDealOrder != null).ToListAsync();
        foreach (var p in dealProducts)
        {
            p.DiscountStart = dto.StartsAt;
            p.DiscountEnd = dto.EndsAt;
        }

        LogAction("Haftanın Fırsatı ayarı güncellendi",
            $"Başlık: “{config.Title}”, Başlangıç: {(dto.StartsAt?.ToString("u") ?? "hemen")}, Bitiş: {(dto.EndsAt?.ToString("u") ?? "yok")}, Aktif: {dto.IsActive}. {dealProducts.Count} ürünün indirim aralığı güncellendi.");

        await _context.SaveChangesAsync();
        return Ok(new { message = "Ayarlar kaydedildi." });
    }

    // ===== Admin: vitrine ürün ekle / oran güncelle =====
    [HttpPost("admin/items")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> UpsertItem(WeeklyDealItemUpsertDto dto)
    {
        if (dto.Percent < 1 || dto.Percent > 99)
        {
            return BadRequest(new { message = "İndirim oranı 1-99 arasında olmalı." });
        }

        var product = await _context.Products.FindAsync(dto.ProductId);
        if (product == null || !product.IsActive)
        {
            return BadRequest(new { message = "Ürün bulunamadı ya da pasif." });
        }

        var config = await GetOrCreateConfigAsync();

        // Sıra verilmediyse listenin sonuna ekle (en büyük mevcut sıradan bir fazlası)
        int order = dto.SortOrder
            ?? (await _context.Products.MaxAsync(p => (int?)p.WeeklyDealOrder) ?? 0) + 1;

        bool yeni = product.WeeklyDealOrder == null;
        product.WeeklyDealOrder = order;
        product.DiscountPrice = PriceFromPercent(product.Price, dto.Percent);
        // İndirim, bölümün planlanan başlangıcında devreye girer (yoksa hemen)
        product.DiscountStart = config.StartsAt ?? DateTime.UtcNow;
        product.DiscountEnd = config.EndsAt;

        LogAction(yeni ? "Haftanın Fırsatı'na ürün eklendi" : "Haftanın Fırsatı ürünü güncellendi",
            $"“{product.Name}” (#{product.Id}): %{dto.Percent} → {product.DiscountPrice} TL (normal {product.Price} TL).");

        await _context.SaveChangesAsync();
        return Ok(new { message = yeni ? "Ürün vitrine eklendi." : "Ürün güncellendi." });
    }

    // ===== Admin: vitrine TOPLU ürün ekle (seçili ürünlere aynı oran) =====
    [HttpPost("admin/items/bulk")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> BulkAdd(WeeklyDealBulkAddDto dto)
    {
        if (dto.Percent < 1 || dto.Percent > 99)
        {
            return BadRequest(new { message = "İndirim oranı 1-99 arasında olmalı." });
        }
        if (dto.ProductIds == null || dto.ProductIds.Count == 0)
        {
            return BadRequest(new { message = "Ürün seçilmedi." });
        }

        var config = await GetOrCreateConfigAsync();

        var products = await _context.Products
            .Where(p => dto.ProductIds.Contains(p.Id) && p.IsActive)
            .ToListAsync();

        if (products.Count == 0)
        {
            return BadRequest(new { message = "Seçilen ürünler bulunamadı ya da pasif." });
        }

        int order = await _context.Products.MaxAsync(p => (int?)p.WeeklyDealOrder) ?? 0;
        int eklenen = 0;
        var now = DateTime.UtcNow;

        // Seçim sırasını koru: kullanıcının işaretlediği sırayla vitrine dizilsinler
        foreach (var p in products.OrderBy(p => dto.ProductIds.IndexOf(p.Id)))
        {
            if (p.WeeklyDealOrder == null)
            {
                p.WeeklyDealOrder = ++order;
                eklenen++;
            }
            p.DiscountPrice = PriceFromPercent(p.Price, dto.Percent);
            p.DiscountStart = config.StartsAt ?? now;
            p.DiscountEnd = config.EndsAt;
        }

        LogAction("Haftanın Fırsatı'na toplu ürün eklendi",
            $"%{dto.Percent} indirimle {products.Count} ürün işlendi ({eklenen} yeni eklendi).");

        await _context.SaveChangesAsync();
        return Ok(new { message = $"{products.Count} ürün %{dto.Percent} indirimle vitrine eklendi." });
    }

    // ===== Admin: vitrinden ürün çıkar (indirimini de kaldırır) =====
    [HttpDelete("admin/items/{productId}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> RemoveItem(int productId)
    {
        var product = await _context.Products.FindAsync(productId);
        if (product == null || product.WeeklyDealOrder == null)
        {
            return NotFound();
        }

        product.WeeklyDealOrder = null;
        product.DiscountPrice = null;
        product.DiscountStart = null;
        product.DiscountEnd = null;

        LogAction("Haftanın Fırsatı'ndan ürün çıkarıldı",
            $"“{product.Name}” (#{product.Id}) vitrinden çıkarıldı, indirimi kaldırıldı.");

        await _context.SaveChangesAsync();
        return Ok(new { message = "Ürün vitrinden çıkarıldı." });
    }

    private void LogAction(string action, string details)
    {
        _context.Logs.Add(new Log
        {
            UserId = int.Parse(User.FindFirst("UserId")!.Value),
            Username = User.Identity?.Name,
            Action = action,
            Details = details,
            Timestamp = DateTime.UtcNow
        });
    }
}
