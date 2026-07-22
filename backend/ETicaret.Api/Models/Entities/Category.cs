namespace ETicaret.Api.Models.Entities;

public class Category
{
    public int Id {get; set;}
    public string Name {get; set; } = string.Empty;

    // Menüde/listede görünme sırası — küçük değer önce gösterilir.
    public int SortOrder { get; set; }

    // Alt kategori desteği: ParentId doluysa bu bir alt kategoridir.
    // Null ise üst (ana) kategoridir.
    public int? ParentId { get; set; }
    public Category? Parent { get; set; }
    public ICollection<Category> Children { get; set; } = new List<Category>();
}


// Ürünleri gruplamak için