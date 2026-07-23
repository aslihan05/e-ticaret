
using ETicaret.Api.Data;
using ETicaret.Api.Models.Dtos;
using ETicaret.Api.Models.Entities;
using ETicaret.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ETicaret.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    private readonly AppDbContext _context;

    public ProductsController(AppDbContext context)
    {
        _context = context;
    }

    // Ürün işlemlerini INSAN OKUR gibi loglar: "hangi ürün" bilgisi doğrudan Details'e yazılır
    // (bkz. #689 gibi kayıtlar). Böylece log satırına bakan admin, detayı açmadan ne olduğunu görür.
    // Middleware bu yollar için ayrıca genel bir log YAZMAZ (çift kayıt olmasın diye "/api/products"
    // başarılı mutasyonlarını atlar), asıl anlamlı kaydı buradaki iş kodu üretir.
    private void UrunLogla(string action, string details)
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

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? search, [FromQuery] bool includeInactive = false)
    {
        bool isAdmin = User.IsInRole("Admin");

        // Admin isterse pasif ürünleri de görür; müşteri her zaman sadece aktifleri görür
        var query = (isAdmin && includeInactive)
            ? _context.Products.AsQueryable()
            : _context.Products.Where(p => p.IsActive);

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(p => p.Name.Contains(search));
        }

        var products = await ProjectProducts(query, isAdmin).ToListAsync();
        return Ok(products);
    }

    // Ürün liste projeksiyonu tek yerde: GetAll ve generic filtre ucu (Filter) bunu paylaşır.
    // _context'e ihtiyaç duyduğu (InStock alt sorguları) için instance metot olarak durur.
    private IQueryable<ProductListItemDto> ProjectProducts(IQueryable<Product> query, bool isAdmin)
    {
        var now = DateTime.UtcNow;
        // Bu andan eskiye kalan sepet rezervasyonları süresi dolmuş sayılır, stoğu kilitlemez
        var rezervasyonSiniri = now - StockService.ReservationWindow;

        // Stok SAYISI sadece admin'e gider; müşteri yalnızca var/yok (inStock) bilgisini görür
        return query.Select(p => new ProductListItemDto
        {
            Id = p.Id,
            Name = p.Name,
            Description = p.Description,
            Price = p.Price,
            ImageUrl = p.ImageUrl,
            CategoryId = p.CategoryId,
            IsActive = p.IsActive,
            // ParentId, "Üst Kategori › Kategori" izinin üst kategori kısmının da
            // tıklanabilir olması için gerekli (adı gönderiliyordu ama id'si yoktu).
            Category = new CategoryMiniDto
            {
                Id = p.Category.Id,
                Name = p.Category.Name,
                ParentId = p.Category.ParentId,
                ParentName = p.Category.Parent != null ? p.Category.Parent.Name : null
            },
            // "Stokta var" demek için depodaki adet yetmez: başkalarının sepetlerinde
            // ayrılmış (süresi geçmemiş) ve onay bekleyen siparişlere sözü verilmiş
            // adetler düşülür. Aksi halde müşteri "stokta var" görüp sepete eklerken hata alırdı.
            InStock = p.Stock
                - _context.CartItems
                    .Where(ci => ci.ProductId == p.Id && ci.AddedAt > rezervasyonSiniri)
                    .Sum(ci => ci.Quantity)
                - _context.OrderItems
                    .Where(oi => oi.ProductId == p.Id
                        && oi.Status == OrderStatus.Pending
                        && oi.Order.Status == OrderStatus.Pending)
                    .Sum(oi => oi.Quantity)
                > 0,
            Stock = isAdmin ? (int?)p.Stock : null,
            Cost = isAdmin ? p.Cost : null,
            HasDiscount = p.DiscountPrice != null && p.DiscountPrice < p.Price
                && (p.DiscountStart == null || p.DiscountStart <= now)
                && (p.DiscountEnd == null || p.DiscountEnd >= now),
            DiscountedPrice = (p.DiscountPrice != null && p.DiscountPrice < p.Price
                && (p.DiscountStart == null || p.DiscountStart <= now)
                && (p.DiscountEnd == null || p.DiscountEnd >= now)) ? p.DiscountPrice : null,
            DiscountPrice = isAdmin ? p.DiscountPrice : null,
            DiscountStart = isAdmin ? p.DiscountStart : null,
            DiscountEnd = isAdmin ? p.DiscountEnd : null,
            // Vitrin kartlarındaki yıldızlar için: hiç yorum yoksa ortalama 0'dır (yıldız basılmaz).
            ReviewCount = p.Reviews.Count(),
            AverageRating = p.Reviews.Any() ? Math.Round(p.Reviews.Average(r => r.Rating), 1) : 0,
            // Admin düzenleme formunun ek görselleri önceden doldurabilmesi için
            ImageUrls = isAdmin ? p.Images.OrderBy(i => i.SortOrder).Select(i => i.Url).ToList() : null
        });
    }

    // Generic çoklu-kolon filtre ucu (Task #3). Frontend'in ürün tablosundaki sütun
    // filtrelerinden (ad, fiyat min/max, kategori, aktiflik, indirim durumu...) ürettiği
    // kurallar tek listede gelir; QueryableFilterExtensions bunları Expression Tree'ye
    // çevirip VERİTABANINDA uygular. Admin'e özel: pasif ürünler ve iç alanlar da döner.
    [HttpPost("filter")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Filter(FilterRequest request)
    {
        var query = _context.Products
            .ApplyFilters(request.Filters)
            .ApplySort(request.SortBy, request.SortDir);

        var products = await ProjectProducts(query, isAdmin: true).ToListAsync();
        return Ok(products);
    }

    // Vitrinin ürün listesi: arama, kategori, fiyat aralığı, sıralama ve SAYFALAMA sunucuda.
    //
    // Neden GetAll'dan ayrı bir uç nokta? İkisinin tüketicisi ve sözleşmesi farklı:
    // GetAll, admin panelinin ihtiyaç duyduğu "tüm katalog" görüntüsüdür (pasif ürünler,
    // stok sayısı, maliyet dahil) ve toplu işlemler tüm listeyi bir arada ister.
    // Burası ise müşteriye açık, sayfalanmış ve yalnız satışa uygun alanları dönen görünüm.
    // Tek uca ikisini birden yaptırmak, cevabın şeklini çağırana göre değiştirmek demekti.
    //
    // Sayfa boyutu dışarıdan geliyor ama sınırsız değil: pageSize=100000 diyen tek bir istek
    // tüm katalogu belleğe çekip sayfalamanın varlık sebebini ortadan kaldırırdı.
    public const int MaxPageSize = 48;
    public const int DefaultPageSize = 12;

    [HttpGet("browse")]
    public async Task<IActionResult> Browse(
        [FromQuery] string? search,
        [FromQuery] int? categoryId,
        [FromQuery] decimal? minPrice,
        [FromQuery] decimal? maxPrice,
        [FromQuery] string? sort,
        [FromQuery] bool discountOnly = false,
        [FromQuery] bool inStockOnly = false,
        [FromQuery] bool newOnly = false,
        [FromQuery] bool reviewedOnly = false,
        [FromQuery] double? minRating = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = DefaultPageSize)
    {
        // Geçersiz sayfa/boyut hata değil, düzeltilir: adres çubuğuna "page=0" yazan
        // kullanıcıya hata sayfası göstermek yerine ilk sayfayı vermek doğru davranış.
        if (page < 1) page = 1;
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var now = DateTime.UtcNow;
        var rezervasyonSiniri = now - StockService.ReservationWindow;

        // "Senin için önerilenler" sıralaması, giriş yapmış kullanıcının geçmişine göre
        // kişiselleşir; bunun için token'daki kullanıcı okunur (browse anonim de çalışır).
        int? userId = null;
        var uidClaim = User.FindFirst("UserId");
        if (uidClaim != null && int.TryParse(uidClaim.Value, out int uid)) userId = uid;

        var query = _context.Products.Where(p => p.IsActive);

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(p => p.Name.Contains(search));
        }

        // Kategori seçimi ALT KATEGORİLERİ de kapsar: ürünler genellikle en alt kategoriye
        // (örn. "Kulaklık") atanır, üst kategoriye (örn. "Elektronik") doğrudan ürün bağlı
        // değildir. Bu yüzden üst kategori seçildiğinde tüm alt (ve alt-alt) kategorilerin
        // ürünleri de gelir — aksi halde "Elektronik" tıklanınca hiç ürün görünmüyordu.
        if (categoryId != null)
        {
            var kategoriAgaci = await _context.Categories
                .Select(c => new { c.Id, c.ParentId })
                .ToListAsync();

            var hedefKategoriIds = new HashSet<int> { categoryId.Value };
            bool yeniEklendi = true;
            while (yeniEklendi)
            {
                yeniEklendi = false;
                foreach (var c in kategoriAgaci)
                {
                    if (c.ParentId != null && hedefKategoriIds.Contains(c.ParentId.Value)
                        && hedefKategoriIds.Add(c.Id))
                    {
                        yeniEklendi = true;
                    }
                }
            }

            query = query.Where(p => hedefKategoriIds.Contains(p.CategoryId));
        }

        // İndirimin "aktif" olması fiyatın düşük olmasına DEĞİL, tarih aralığının da
        // tutmasına bağlı; süresi geçmiş indirim indirim değildir.
        if (discountOnly)
        {
            query = query.Where(p => p.DiscountPrice != null && p.DiscountPrice < p.Price
                && (p.DiscountStart == null || p.DiscountStart <= now)
                && (p.DiscountEnd == null || p.DiscountEnd >= now));
        }

        // Fiyat filtresi ve sıralaması GEÇERLİ fiyat üzerinden yapılır: 1000 TL'lik ama
        // 400 TL'ye inmiş bir ürün, "max 500 TL" arayan kullanıcının sonucunda çıkmalı.
        // (Tarayıcıdaki `eff = discountedPrice ?? price` mantığının SQL'e çevrilmiş hali.)
        if (minPrice != null)
        {
            query = query.Where(p => (p.DiscountPrice != null && p.DiscountPrice < p.Price
                && (p.DiscountStart == null || p.DiscountStart <= now)
                && (p.DiscountEnd == null || p.DiscountEnd >= now)
                    ? p.DiscountPrice!.Value : p.Price) >= minPrice);
        }

        if (maxPrice != null)
        {
            query = query.Where(p => (p.DiscountPrice != null && p.DiscountPrice < p.Price
                && (p.DiscountStart == null || p.DiscountStart <= now)
                && (p.DiscountEnd == null || p.DiscountEnd >= now)
                    ? p.DiscountPrice!.Value : p.Price) <= maxPrice);
        }

        // "Sadece stokta olanlar": müsait adet (depo − sepet rezervasyonları − onay bekleyen
        // siparişler) 0'dan büyük olan ürünler. inStock kartta gösterilen bilgiyle aynı hesap;
        // burada Where'e taşınarak tükenmiş ürünler listeden hiç gelmez.
        if (inStockOnly)
        {
            query = query.Where(p => p.Stock
                - _context.CartItems
                    .Where(ci => ci.ProductId == p.Id && ci.AddedAt > rezervasyonSiniri)
                    .Sum(ci => ci.Quantity)
                - _context.OrderItems
                    .Where(oi => oi.ProductId == p.Id
                        && oi.Status == OrderStatus.Pending
                        && oi.Order.Status == OrderStatus.Pending)
                    .Sum(oi => oi.Quantity)
                > 0);
        }

        // "Sadece yeni gelenler": son 7 günde kataloğa eklenen ürünler. new-arrivals hızlı
        // butonuyla aynı eşik, ama burada bir FİLTRE olarak — kategori/fiyat gibi diğer
        // filtrelerle ve istenen herhangi bir sıralamayla birlikte kullanılabilir.
        if (newOnly)
        {
            query = query.Where(p => p.CreatedAt >= now.AddDays(-7));
        }

        // "Sadece değerlendirilenler": en az bir yorumu olan ürünler. minRating'ten farkı,
        // bir puan eşiği dayatmaması — "hakkında yorum var mı" sorusudur, "kaç yıldız" değil.
        if (reviewedOnly)
        {
            query = query.Where(p => p.Reviews.Any());
        }

        // "En az X yıldız": yalnızca ortalama puanı eşiğe ulaşan ürünler. Hiç yorumu olmayan
        // ürün 0 sayılır (puanlı filtrede elenir) — yorumsuz ürünü "4+ yıldız" listesine
        // koymak yanıltıcı olurdu.
        if (minRating != null)
        {
            query = query.Where(p => p.Reviews.Any()
                && p.Reviews.Average(r => r.Rating) >= minRating);
        }

        // Hızlı butonların anlamı, kataloğu yeniden sıralamak DEĞİL, gerçek bir "top liste"
        // göstermektir: "En Çok Satanlar" yalnızca satılmış ürünleri, "En Çok Favori Seçilenler"
        // yalnızca en az bir kez favorilenmiş ürünleri kapsar. Aksi halde bu listeler tüm
        // katalogun hafifçe yeniden sıralanmış hâli olur ve kullanıcıya "hiçbir şey değişmedi"
        // gibi görünürdü. (Öneri listesi ise geniş kalır; kişiselleştirme sıralamada yapılır.)
        if (sort == "best-selling")
        {
            query = query.Where(p => _context.OrderItems.Any(oi => oi.ProductId == p.Id
                && oi.Status == OrderStatus.Approved
                && oi.Order.Status != OrderStatus.Cancelled
                && oi.Order.Status != OrderStatus.Rejected));
        }
        else if (sort == "most-favorited")
        {
            query = query.Where(p => p.Favorites.Any());
        }
        // "Yeni Gelenler": son 7 günde kataloğa eklenen ürünler. Diğer hızlı butonlar gibi
        // bu da bir "top liste"dir — sıralamayı değil, GÖRÜNEN KÜMEYİ daraltır ki kullanıcı
        // gerçekten yeni ürünleri görsün, tüm kataloğun tarihe göre dizilişini değil.
        else if (sort == "new-arrivals")
        {
            query = query.Where(p => p.CreatedAt >= now.AddDays(-7));
        }

        // "Senin için önerilenler": müşterinin geçmişte ETKİLEŞİME girdiği ürünler —
        // daha önce satın aldığı + sepete eklediği + favorilediği ürünlerin birleşimi.
        // Giriş yapmamış ya da hiç geçmişi olmayan kullanıcıda liste boş kalmasın diye
        // filtre uygulanmaz; sıralama popüler ürünleri getirir.
        if (sort == "recommended" && userId != null)
        {
            var favUrun = _context.Favorites.Where(f => f.UserId == userId).Select(f => f.ProductId);
            var sepetUrun = _context.CartItems.Where(c => c.UserId == userId).Select(c => c.ProductId);
            var siparisUrun = _context.OrderItems.Where(oi => oi.Order.UserId == userId).Select(oi => oi.ProductId);
            var ilgiliUrunIds = await favUrun.Concat(sepetUrun).Concat(siparisUrun).Distinct().ToListAsync();

            // Geçmiş varsa yalnızca bu ürünleri göster; yoksa (yeni kullanıcı) filtre koyma.
            if (ilgiliUrunIds.Count > 0)
            {
                query = query.Where(p => ilgiliUrunIds.Contains(p.Id));
            }
        }

        // Toplam, sayfalamadan ÖNCE ve filtrelerden SONRA sayılır: "37 üründen 1-12"
        // yazabilmek ve son sayfayı bilebilmek için gereken sayı bu.
        int total = await query.CountAsync();

        query = sort switch
        {
            "price-asc" => query.OrderBy(p => p.DiscountPrice != null && p.DiscountPrice < p.Price
                && (p.DiscountStart == null || p.DiscountStart <= now)
                && (p.DiscountEnd == null || p.DiscountEnd >= now)
                    ? p.DiscountPrice!.Value : p.Price),
            "price-desc" => query.OrderByDescending(p => p.DiscountPrice != null && p.DiscountPrice < p.Price
                && (p.DiscountStart == null || p.DiscountStart <= now)
                && (p.DiscountEnd == null || p.DiscountEnd >= now)
                    ? p.DiscountPrice!.Value : p.Price),
            // Hiç yorumu olmayan ürün 0 ortalamayla en sona düşer — "en çok beğenilen"
            // listesinin başında puansız ürün olması saçma olurdu.
            "rating-desc" => query
                .OrderByDescending(p => p.Reviews.Any() ? p.Reviews.Average(r => r.Rating) : 0)
                .ThenByDescending(p => p.Reviews.Count()),
            // "En çok satanlar": gerçekten satılmış (onaylı kalem, iptal/reddedilmemiş sipariş)
            // adetlerin toplamına göre. Hiç satılmayan ürün 0 ile en sona düşer.
            "best-selling" => query
                .OrderByDescending(p => _context.OrderItems
                    .Where(oi => oi.ProductId == p.Id
                        && oi.Status == OrderStatus.Approved
                        && oi.Order.Status != OrderStatus.Cancelled
                        && oi.Order.Status != OrderStatus.Rejected)
                    .Sum(oi => (int?)oi.Quantity) ?? 0),
            // "En çok favori seçilenler": ürünü favorileyen kullanıcı sayısına göre.
            "most-favorited" => query
                .OrderByDescending(p => p.Favorites.Count()),
            // "Yeni Gelenler": en son eklenen en üstte.
            "new-arrivals" => query
                .OrderByDescending(p => p.CreatedAt),
            // "Senin için önerilenler": kullanıcının ilgilendiği ürünler (yukarıda filtrelendi),
            // popülerlik (favori) ve puana göre sıralanır. Geçmişi yoksa liste tüm katalogdan
            // popülerleri gösterir.
            "recommended" => query
                .OrderByDescending(p => p.Favorites.Count())
                .ThenByDescending(p => p.Reviews.Any() ? p.Reviews.Average(r => r.Rating) : 0),
            // Varsayılan sıra Id: sayfalamada ZORUNLU. Sırasız bir sorguda veritabanı
            // satırları hangi sırada döndüreceğini garanti etmez; 2. sayfada 1. sayfadaki
            // ürünün tekrar çıkması (ya da bir ürünün hiç görünmemesi) buradan doğar.
            _ => query.OrderBy(p => p.Id)
        };

        // Eşit değerli satırlarda (aynı fiyat, aynı puan) sıra yine belirsiz kalır;
        // Id ikinci anahtar olarak her zaman kesin ve tekrarlanabilir bir sıra verir.
        query = ((IOrderedQueryable<Product>)query).ThenBy(p => p.Id);

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new ProductBrowseItemDto
            {
                Id = p.Id,
                Name = p.Name,
                Description = p.Description,
                Price = p.Price,
                ImageUrl = p.ImageUrl,
                CategoryId = p.CategoryId,
                InStock = p.Stock
                    - _context.CartItems
                        .Where(ci => ci.ProductId == p.Id && ci.AddedAt > rezervasyonSiniri)
                        .Sum(ci => ci.Quantity)
                    - _context.OrderItems
                        .Where(oi => oi.ProductId == p.Id
                            && oi.Status == OrderStatus.Pending
                            && oi.Order.Status == OrderStatus.Pending)
                        .Sum(oi => oi.Quantity)
                    > 0,
                HasDiscount = p.DiscountPrice != null && p.DiscountPrice < p.Price
                    && (p.DiscountStart == null || p.DiscountStart <= now)
                    && (p.DiscountEnd == null || p.DiscountEnd >= now),
                DiscountedPrice = (p.DiscountPrice != null && p.DiscountPrice < p.Price
                    && (p.DiscountStart == null || p.DiscountStart <= now)
                    && (p.DiscountEnd == null || p.DiscountEnd >= now)) ? p.DiscountPrice : null,
                ReviewCount = p.Reviews.Count(),
                AverageRating = p.Reviews.Any() ? Math.Round(p.Reviews.Average(r => r.Rating), 1) : 0
            })
            .ToListAsync();

        return Ok(new PagedResultDto<ProductBrowseItemDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            Total = total,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize)
        });
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        bool isAdmin = User.IsInRole("Admin");
        var now = DateTime.UtcNow;
        var rezervasyonSiniri = DateTime.UtcNow - StockService.ReservationWindow;

        var product = await _context.Products
            .Where(p => p.Id == id && (p.IsActive || isAdmin))
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.Description,
                p.Price,
                p.ImageUrl,
                p.CategoryId,
                p.IsActive,
                // ParentId, "Üst Kategori › Kategori" izinin üst kategori kısmının da
                // tıklanabilir olması için gerekli (adı gönderiliyordu ama id'si yoktu).
                Category = new
                {
                    p.Category.Id,
                    p.Category.Name,
                    p.Category.ParentId,
                    ParentName = p.Category.Parent != null ? p.Category.Parent.Name : null
                },
                // "Stokta var" demek için depodaki adet yetmez: başkalarının sepetlerinde
                // ayrılmış (süresi geçmemiş) ve onay bekleyen siparişlere sözü verilmiş
                // adetler düşülür. Aksi halde müşteri "stokta var" görüp sepete eklerken hata alırdı.
                InStock = p.Stock
                    - _context.CartItems
                        .Where(ci => ci.ProductId == p.Id && ci.AddedAt > rezervasyonSiniri)
                        .Sum(ci => ci.Quantity)
                    - _context.OrderItems
                        .Where(oi => oi.ProductId == p.Id
                            && oi.Status == OrderStatus.Pending
                            && oi.Order.Status == OrderStatus.Pending)
                        .Sum(oi => oi.Quantity)
                    > 0,
                Stock = isAdmin ? (int?)p.Stock : null,
                Cost = isAdmin ? p.Cost : null,
                HasDiscount = p.DiscountPrice != null && p.DiscountPrice < p.Price
                    && (p.DiscountStart == null || p.DiscountStart <= now)
                    && (p.DiscountEnd == null || p.DiscountEnd >= now),
                DiscountedPrice = (p.DiscountPrice != null && p.DiscountPrice < p.Price
                    && (p.DiscountStart == null || p.DiscountStart <= now)
                    && (p.DiscountEnd == null || p.DiscountEnd >= now)) ? p.DiscountPrice : null,
                // Yıldız özeti başlıkta gösterilir; dökümü ayrıca /products/{id}/reviews'den gelir.
                ReviewCount = p.Reviews.Count(),
                AverageRating = p.Reviews.Any() ? Math.Round(p.Reviews.Average(r => r.Rating), 1) : 0,
                // Ana görsel + ek görseller: detay sayfasındaki slider bunları sırayla gösterir
                ImageUrls = p.Images.OrderBy(i => i.SortOrder).Select(i => i.Url).ToList()
            })
            .FirstOrDefaultAsync();

        if (product == null)
        {
            return NotFound();
        }

        return Ok(product);
    }

    // Müşterinin bu üründeki KENDİ alım geçmişi. Admin'deki products/{id}/orders
    // ürünün tüm hareketlerini döner; burada öyle bir şey yapılamaz — başka
    // müşterilerin ne alıp ne ödediği ifşa olur. Sorgu daima token'daki kullanıcıya
    // kilitlenir; id dışarıdan alınmaz.
    [HttpGet("{id}/my-history")]
    [Authorize]
    public async Task<IActionResult> GetMyHistory(int id)
    {
        var userId = int.Parse(User.FindFirst("UserId")!.Value);

        var items = await _context.OrderItems
            .Where(oi => oi.ProductId == id && oi.Order.UserId == userId)
            .OrderByDescending(oi => oi.Order.CreatedAt)
            .Select(oi => new
            {
                oi.OrderId,
                // CreatedAt veritabanından Kind=Unspecified döner; işaretlemezsek JSON'a
                // "Z" olmadan yazılır ve tarayıcı yerel saat sanıp tarihi kaydırır.
                CreatedAt = DateTime.SpecifyKind(oi.Order.CreatedAt, DateTimeKind.Utc),
                oi.Quantity,
                oi.UnitPrice,
                LineTotal = oi.UnitPrice * oi.Quantity,
                ItemStatus = oi.Status,
                OrderStatus = oi.Order.Status
            })
            .ToListAsync();

        // Özet yalnızca gerçekleşen alımları sayar: reddedilen kalem ya da iptal/reddedilen
        // siparişteki kalem "aldım" sayılmaz.
        var alinan = items
            .Where(i => i.ItemStatus == OrderStatus.Approved
                && i.OrderStatus != OrderStatus.Cancelled
                && i.OrderStatus != OrderStatus.Rejected)
            .ToList();

        return Ok(new
        {
            TotalOrders = alinan.Select(i => i.OrderId).Distinct().Count(),
            TotalQty = alinan.Sum(i => i.Quantity),
            TotalSpent = alinan.Sum(i => i.LineTotal),
            // "Daha ucuza almış mıydım?" sorusunun cevabı — sadece kendi ödediği fiyatlar üzerinden
            LowestUnitPrice = alinan.Count > 0 ? alinan.Min(i => i.UnitPrice) : (decimal?)null,
            LastPurchasedAt = alinan.Count > 0 ? alinan.Max(i => i.CreatedAt) : (DateTime?)null,
            Items = items
        });
    }

[HttpPost]
[Authorize(Roles = "Admin")]
public async Task<IActionResult> Create(ProductDto dto)
{
    // Aynı isimde pasif (silinmiş) ürün varsa yenisini açmak yerine onu canlandır.
    var inactive = await _context.Products
        .FirstOrDefaultAsync(p => !p.IsActive && p.Name == dto.Name);

    if (inactive != null)
    {
        inactive.IsActive = true;
        // Yeniden satışa açılan ürün de "yeni gelen" sayılır: tekrar vitrine çıktığı an.
        inactive.CreatedAt = DateTime.UtcNow;
        await ApplyDto(inactive, dto);
        UrunLogla("Ürün eklendi", $"“{inactive.Name}” (#{inactive.Id}) ürünü yeniden satışa açıldı (aynı adla mevcut pasif kayıt canlandırıldı). Stok: {inactive.Stock}, Fiyat: {inactive.Price} TL.");
        await _context.SaveChangesAsync();
        return Ok(inactive);
    }

    var product = new Product { CreatedAt = DateTime.UtcNow };
    await ApplyDto(product, dto);

    _context.Products.Add(product);
    await _context.SaveChangesAsync();   // Id burada oluşur; log Details'inde #Id kullanabilmek için önce kaydedilir

    UrunLogla("Ürün eklendi", $"“{product.Name}” (#{product.Id}) ürünü eklendi. Kategori #{product.CategoryId}, Stok: {product.Stock}, Fiyat: {product.Price} TL.");
    await _context.SaveChangesAsync();
    return Ok(product);
}


[HttpPut("{id}")]
[Authorize(Roles = "Admin")]
public async Task<IActionResult> Update(int id, ProductDto dto)
{
    var product = await _context.Products.FindAsync(id);
    if (product == null)
    {
        return NotFound();
    }

    await ApplyDto(product, dto);

    UrunLogla("Ürün güncellendi", $"“{product.Name}” (#{product.Id}) ürünü güncellendi. Kategori #{product.CategoryId}, Stok: {product.Stock}, Fiyat: {product.Price} TL.");
    await _context.SaveChangesAsync();
    return Ok(product);
}

private async Task ApplyDto(Product product, ProductDto dto)
{
    product.Name = dto.Name;
    product.Description = dto.Description;
    product.Price = dto.Price;
    product.Cost = dto.Cost;
    product.Stock = dto.Stock;
    product.ImageUrl = dto.ImageUrl;
    product.CategoryId = dto.CategoryId;
    product.DiscountPrice = dto.DiscountPrice;
    product.DiscountStart = dto.DiscountStart;
    product.DiscountEnd = dto.DiscountEnd;

    // Ek görseller: (varsa) eskilerini sil, dto'dakileri sırayla yeniden ekle.
    // product.Id == 0 ise henüz kaydedilmemiş yeni bir üründür, silinecek eski görsel yoktur.
    if (product.Id != 0)
    {
        var oldImages = await _context.ProductImages
            .Where(i => i.ProductId == product.Id)
            .ToListAsync();
        _context.ProductImages.RemoveRange(oldImages);
    }

    if (dto.ImageUrls != null)
    {
        int order = 0;
        foreach (var url in dto.ImageUrls.Where(u => !string.IsNullOrWhiteSpace(u)))
        {
            product.Images.Add(new ProductImage { Url = url.Trim(), SortOrder = order++ });
        }
    }
}

// Sadece stok güncelle (Ürünler sekmesindeki hızlı stok kutusu için)
[HttpPut("{id}/stock")]
[Authorize(Roles = "Admin")]
public async Task<IActionResult> UpdateStock(int id, StockUpdateDto dto)
{
    var product = await _context.Products.FindAsync(id);
    if (product == null)
    {
        return NotFound();
    }

    if (dto.Stock < 0)
    {
        return BadRequest(new { message = "Stok negatif olamaz." });
    }

    int eskiStok = product.Stock;
    product.Stock = dto.Stock;
    UrunLogla("Stok güncellendi", $"“{product.Name}” (#{product.Id}) stoğu {eskiStok} → {dto.Stock} olarak ayarlandı.");
    await _context.SaveChangesAsync();
    return Ok(new { message = "Stok güncellendi.", product.Id, product.Stock });
}

// İndirim planla / kaldır (tüm alanlar null gelirse indirim kalkar)
[HttpPut("{id}/discount")]
[Authorize(Roles = "Admin")]
public async Task<IActionResult> UpdateDiscount(int id, DiscountDto dto)
{
    var product = await _context.Products.FindAsync(id);
    if (product == null)
    {
        return NotFound();
    }

    if (dto.DiscountPrice != null && dto.DiscountPrice >= product.Price)
    {
        return BadRequest(new { message = "İndirimli fiyat normal fiyattan düşük olmalı." });
    }

    product.DiscountPrice = dto.DiscountPrice;
    product.DiscountStart = dto.DiscountStart;
    product.DiscountEnd = dto.DiscountEnd;

    // Loglama: kim, ne zaman, hangi ürünü indirime soktu/kaldırdı (#8)
    int adminId = int.Parse(User.FindFirst("UserId")!.Value);
    _context.Logs.Add(new Log
    {
        UserId = adminId,
        Action = dto.DiscountPrice == null ? "İndirim kaldırıldı" : "İndirim uygulandı",
        Details = dto.DiscountPrice == null
            ? $"{product.Name} (#{product.Id}) ürününün indirimi kaldırıldı."
            : $"{product.Name} (#{product.Id}): {product.Price} TL → {dto.DiscountPrice} TL",
        Timestamp = DateTime.UtcNow
    });

    await _context.SaveChangesAsync();
    return Ok(new { message = dto.DiscountPrice == null ? "İndirim kaldırıldı." : "İndirim kaydedildi." });
}

// Bir kategorideki tüm aktif ürünlere yüzde oranıyla indirim uygula (#5)
[HttpPut("category/{categoryId}/discount")]
[Authorize(Roles = "Admin")]
public async Task<IActionResult> ApplyCategoryDiscount(int categoryId, CategoryDiscountDto dto)
{
    if (dto.Percent <= 0 || dto.Percent >= 100)
    {
        return BadRequest(new { message = "İndirim oranı 1-99 arasında olmalı." });
    }

    var products = await _context.Products
        .Where(p => p.IsActive && p.CategoryId == categoryId)
        .ToListAsync();

    if (products.Count == 0)
    {
        return BadRequest(new { message = "Bu kategoride aktif ürün yok." });
    }

    foreach (var p in products)
    {
        // İndirimli fiyat = fiyat * (100 - yüzde) / 100, 2 basamağa yuvarlanır
        p.DiscountPrice = Math.Round(p.Price * (100 - dto.Percent) / 100m, 2);
        p.DiscountStart = dto.DiscountStart;
        p.DiscountEnd = dto.DiscountEnd;
    }

    int adminUserId = int.Parse(User.FindFirst("UserId")!.Value);
    _context.Logs.Add(new Log
    {
        UserId = adminUserId,
        Action = "Kategori indirimi uygulandı",
        Details = $"Kategori #{categoryId}: %{dto.Percent} indirim {products.Count} ürüne uygulandı.",
        Timestamp = DateTime.UtcNow
    });

    await _context.SaveChangesAsync();
    return Ok(new { message = $"%{dto.Percent} indirim {products.Count} ürüne uygulandı." });
}

// Seçilen ürünlere topluca indirim uygula/kaldır (çoklu seçim, kategori fark etmez) (#1)
[HttpPut("discount/bulk")]
[Authorize(Roles = "Admin")]
public async Task<IActionResult> BulkDiscount(BulkDiscountDto dto)
{
    if (dto.Ids == null || dto.Ids.Count == 0)
    {
        return BadRequest(new { message = "Ürün seçilmedi." });
    }

    bool clear = dto.Percent == null || dto.Percent <= 0;
    if (!clear && dto.Percent >= 100)
    {
        return BadRequest(new { message = "İndirim oranı 1-99 arasında olmalı." });
    }

    var products = await _context.Products
        .Where(p => dto.Ids.Contains(p.Id))
        .ToListAsync();

    foreach (var p in products)
    {
        if (clear)
        {
            p.DiscountPrice = null;
            p.DiscountStart = null;
            p.DiscountEnd = null;
        }
        else
        {
            p.DiscountPrice = Math.Round(p.Price * (100 - dto.Percent!.Value) / 100m, 2);
            p.DiscountStart = dto.DiscountStart;
            p.DiscountEnd = dto.DiscountEnd;
        }
    }

    int adminUserId = int.Parse(User.FindFirst("UserId")!.Value);
    _context.Logs.Add(new Log
    {
        UserId = adminUserId,
        Action = clear ? "Toplu indirim kaldırıldı" : "Toplu indirim uygulandı",
        Details = clear
            ? $"{products.Count} ürünün indirimi kaldırıldı."
            : $"%{dto.Percent} indirim {products.Count} seçili ürüne uygulandı.",
        Timestamp = DateTime.UtcNow
    });

    await _context.SaveChangesAsync();
    return Ok(new { message = clear ? $"{products.Count} üründe indirim kaldırıldı." : $"%{dto.Percent} indirim {products.Count} ürüne uygulandı." });
}

// Pasif ürünü yeniden satışa aç
[HttpPut("{id}/activate")]
[Authorize(Roles = "Admin")]
public async Task<IActionResult> Activate(int id)
{
    var product = await _context.Products.FindAsync(id);
    if (product == null)
    {
        return NotFound();
    }

    product.IsActive = true;
    UrunLogla("Ürün aktifleştirildi", $"“{product.Name}” (#{product.Id}) ürünü yeniden satışa açıldı.");
    await _context.SaveChangesAsync();
    return Ok(new { message = "Ürün yeniden aktif." });
}


// Hard delete yerine soft delete: geçmiş siparişlerin satırları korunur,
// ürün listeden kalkar, istenirse tekrar aktifleştirilebilir.
// Stokta mal varken pasifleştirmek, sayılan malı kayıtta yok göstermek demektir; bu yüzden
// kural kalktı değil, ONAYA bağlandı: istek force=true ile gelmezse uyarıyla reddedilir.
// (Panel bu cevabı görünce admine "stokta X adet var, yine de kaldırılsın mı?" diye sorar.)
// Böylece yanlışlıkla kapatma korunurken, sezon sonu gibi durumlarda stoklu ürün de
// tek adımda satıştan çekilebilir — stok bilgisi silinmez, ürün yalnızca listelerden düşer.
[HttpDelete("{id}")]
[Authorize(Roles = "Admin")]
public async Task<IActionResult> Delete(int id, [FromQuery] bool force = false)
{
    var product = await _context.Products.FindAsync(id);
    if (product == null)
    {
        return NotFound();
    }

    if (product.Stock > 0 && !force)
    {
        return BadRequest(new
        {
            message = $"Stokta {product.Stock} adet var; yine de pasifleştirmek için onay gerekiyor.",
            requiresConfirmation = true,
            stock = product.Stock
        });
    }

    product.IsActive = false;
    UrunLogla("Ürün pasife alındı",
        product.Stock > 0
            ? $"“{product.Name}” (#{product.Id}) ürünü stokta {product.Stock} adet varken satıştan kaldırıldı (onaylı pasifleştirme)."
            : $"“{product.Name}” (#{product.Id}) ürünü satıştan kaldırıldı (pasife alındı).");
    await _context.SaveChangesAsync();
    return NoContent();
}

}
