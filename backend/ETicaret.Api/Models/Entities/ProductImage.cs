namespace ETicaret.Api.Models.Entities;

// Ürünün ana görseli (Product.ImageUrl) dışında, detay sayfasındaki slider'da
// gösterilecek ek görseller. SortOrder, slider'daki sırayı belirler.
public class ProductImage
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public string Url { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}
