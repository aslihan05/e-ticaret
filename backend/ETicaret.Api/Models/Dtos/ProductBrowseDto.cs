namespace ETicaret.Api.Models.Dtos;

// Vitrindeki bir ürün kartının ihtiyaç duyduğu alanlar.
// Admin'e özel alanlar (Stock sayısı, Cost) burada YOKTUR: bu uç nokta herkese açık,
// olsalardı maliyet bilgisi de cevapla birlikte dışarı sızardı.
public class ProductBrowseItemDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public string? ImageUrl { get; set; }
    public int CategoryId { get; set; }
    public bool InStock { get; set; }
    public bool HasDiscount { get; set; }
    public decimal? DiscountedPrice { get; set; }
    public int ReviewCount { get; set; }
    public double AverageRating { get; set; }
}

// Sayfalanmış cevabın zarfı. Frontend'in "kaç sayfa var, kaçıncıdayım" sorularının
// cevabını tahmin etmesi değil sunucudan öğrenmesi gerekir; toplam kayıt sayısı
// (Total) olmadan sayfa düğmeleri çizilemez.
public class PagedResultDto<T>
{
    public List<T> Items { get; set; } = new();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int Total { get; set; }
    public int TotalPages { get; set; }
}
