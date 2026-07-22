namespace ETicaret.Api.Models.Dtos;

// Ürün liste/katalog projeksiyonu. Hem GetAll (tüm katalog) hem de generic filtre ucu
// (POST /products/filter) aynı şekli döndürsün diye tek yerde tanımlı — böylece frontend
// her iki yanıtı da aynı kodla işleyebilir.
//
// Not: Ham Product entity'si yerine bu DTO döner; Cost/Stock/DiscountPrice gibi iç alanlar
// yalnızca admin için doldurulur (müşteriye null gider).
public class ProductListItemDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public string? ImageUrl { get; set; }
    public int CategoryId { get; set; }
    public bool IsActive { get; set; }
    public CategoryMiniDto Category { get; set; } = null!;

    public bool InStock { get; set; }
    public int? Stock { get; set; }
    public decimal? Cost { get; set; }

    public bool HasDiscount { get; set; }
    public decimal? DiscountedPrice { get; set; }
    public decimal? DiscountPrice { get; set; }
    public DateTime? DiscountStart { get; set; }
    public DateTime? DiscountEnd { get; set; }

    public int ReviewCount { get; set; }
    public double AverageRating { get; set; }

    public List<string>? ImageUrls { get; set; }
}

public class CategoryMiniDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? ParentId { get; set; }
    public string? ParentName { get; set; }
}
