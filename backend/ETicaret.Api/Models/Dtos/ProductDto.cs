namespace ETicaret.Api.Models.Dtos;

public class ProductDto
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public decimal? Cost { get; set; }
    public int Stock { get; set; }
    public string? ImageUrl { get; set; }
    // Ana görsel dışındaki ek görseller (detay sayfası slider'ı), sırasıyla
    public List<string>? ImageUrls { get; set; }
    public int CategoryId { get; set; }
    public decimal? DiscountPrice { get; set; }
    public DateTime? DiscountStart { get; set; }
    public DateTime? DiscountEnd { get; set; }
}