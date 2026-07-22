namespace ETicaret.Api.Models.Dtos;

public class CategoryDto
{
    public string Name { get; set; } = string.Empty;
    public int? ParentId { get; set; }   // Doluysa alt kategori; boşsa ana kategori
    public int SortOrder { get; set; }   // Menüdeki görünme sırası (küçük değer önce)
}
