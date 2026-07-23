namespace ETicaret.Api.Models.Entities;

public class Product
{
    public int Id {get; set;}
    public string Name {get; set; } = string.Empty;
    public string? Description {get; set;}
    public decimal Price { get; set; }  // decimal finansal hesaplamalarda daha çok tercih edilir
    public decimal? Cost { get; set; }   // Maliyet: kâr = efektif satış fiyatı - maliyet
    public int Stock { get; set; }
    public string? ImageUrl { get; set; }
    
    
    public bool IsActive {get; set; } = true;
    // Silmek yerine pasife çekme imkanı.
    // Productscontroller'ın sadece aktifleri listelemesi.

    // Ürünün kataloğa eklendiği an (UTC). "Yeni Gelenler" listesi bu tarihe göre süzülür:
    // son 7 günde eklenenler. Veritabanı varsayılanı (SYSUTCDATETIME) sayesinde bu sütun
    // eklenmeden önceki ürünler de tarihsiz kalmaz.
    public DateTime CreatedAt { get; set; }

    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;

    // Ana görsel (ImageUrl) dışındaki ek görseller — detay sayfasındaki slider için.
    public ICollection<ProductImage> Images { get; set; } = new List<ProductImage>();

    // Müşteri yorumları ve puanları — ortalama puan bunlardan hesaplanır.
    public ICollection<Review> Reviews { get; set; } = new List<Review>();

    // Bu ürünü favorilerine ekleyen kullanıcıların kayıtları.
    public ICollection<Favorite> Favorites { get; set; } = new List<Favorite>();

    // İndirim planlama: DiscountPrice doluysa ve tarih aralığı tutuyorsa indirim aktiftir.
    // Tarihler null bırakılabilir -> süresiz indirim.
    public decimal? DiscountPrice { get; set; }
    public DateTime? DiscountStart { get; set; }
    public DateTime? DiscountEnd { get; set; }

    // "Haftanın Fırsatı" vitrini: null ise ürün bu bölümde DEĞİL. Doluysa bölümdeki
    // sıralamayı belirler (küçük önce). Bölüme eklenen ürünün indirimi gerçek indirim
    // sistemiyle (DiscountPrice/Start/End) uygulanır; bu sütun yalnızca "hangi ürünler
    // vitrinde ve hangi sırada" sorusunu yanıtlar, indirimin kendisini değil.
    public int? WeeklyDealOrder { get; set; }

    public bool IsDiscountActive(DateTime now) =>
        DiscountPrice != null && DiscountPrice < Price
        && (DiscountStart == null || DiscountStart <= now)
        && (DiscountEnd == null || DiscountEnd >= now);

    public decimal EffectivePrice(DateTime now) =>
        IsDiscountActive(now) ? DiscountPrice!.Value : Price;
}


// Kataloğun merkezi.