namespace ETicaret.Api.Models.Entities;

public class Product
{
    public int Id {get; set;}
    public string Name {get; set; } = string.Empty;
    public string? Description {get; set;}
    public decimal Price { get; set; }  // decimal finansal hesaplamalarda daha çok tercih edilir
    public int Stock { get; set; }
    public string? ImageUrl { get; set; }
    
    
    public bool IsActive {get; set; } = true;
    // Silmek yerine pasife çekme imkanı.
    // Productscontroller'ın sadece aktifleri listelemesi.

    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;

    // İndirim planlama: DiscountPrice doluysa ve tarih aralığı tutuyorsa indirim aktiftir.
    // Tarihler null bırakılabilir -> süresiz indirim.
    public decimal? DiscountPrice { get; set; }
    public DateTime? DiscountStart { get; set; }
    public DateTime? DiscountEnd { get; set; }

    public bool IsDiscountActive(DateTime now) =>
        DiscountPrice != null && DiscountPrice < Price
        && (DiscountStart == null || DiscountStart <= now)
        && (DiscountEnd == null || DiscountEnd >= now);

    public decimal EffectivePrice(DateTime now) =>
        IsDiscountActive(now) ? DiscountPrice!.Value : Price;
}


// Kataloğun merkezi.