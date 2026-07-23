namespace ETicaret.Api.Models.Dtos;

// Ziyaretçiye dönen "Haftanın Fırsatı" görünümü: bölüm başlığı, geri sayım bitişi ve
// vitrindeki ürünler (indirimli fiyatlarıyla). Yalnızca aktif konfig + aktif ürünler döner.
public class WeeklyDealPublicDto
{
    public string Title { get; set; } = "Haftanın Fırsatı";
    public DateTime? StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }
    public bool IsActive { get; set; }
    public List<WeeklyDealItemPublicDto> Items { get; set; } = new();
}

// Vitrin kartı, sezon indirimi kartlarıyla aynı davranışa sahip (tıklayınca ürün detay
// kutusu açılır); o kutunun ihtiyaç duyduğu açıklama/puan alanları da burada döner.
public class WeeklyDealItemPublicDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }
    public decimal Price { get; set; }
    public decimal DiscountedPrice { get; set; }
    public int DiscountPercent { get; set; }
    public bool HasDiscount { get; set; }
    public bool InStock { get; set; }
    public int ReviewCount { get; set; }
    public double AverageRating { get; set; }
}

// Admin panelinin gördüğü tam görünüm: ayarlar + vitrindeki ürünler (yönetim alanlarıyla).
public class WeeklyDealAdminDto
{
    public string Title { get; set; } = "Haftanın Fırsatı";
    public DateTime? StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }
    public bool IsActive { get; set; }
    public List<WeeklyDealItemAdminDto> Items { get; set; } = new();
}

public class WeeklyDealItemAdminDto
{
    public int ProductId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public decimal Price { get; set; }
    public decimal? DiscountedPrice { get; set; }
    public int DiscountPercent { get; set; }
    public int SortOrder { get; set; }
    public string? CategoryName { get; set; }
    public int Stock { get; set; }
}

// Ayar güncelleme (başlık / başlangıç–bitiş tarihi / aktiflik).
public class WeeklyDealSettingsDto
{
    public string? Title { get; set; }
    public DateTime? StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }
    public bool IsActive { get; set; }
}

// Vitrine ürün ekleme / güncelleme: hangi ürün, kaç yüzde indirim, hangi sırada.
public class WeeklyDealItemUpsertDto
{
    public int ProductId { get; set; }
    public int Percent { get; set; }
    public int? SortOrder { get; set; }
}

// Vitrine tek seferde birden çok ürün ekleme: seçili ürünlere aynı oran uygulanır.
public class WeeklyDealBulkAddDto
{
    public List<int> ProductIds { get; set; } = new();
    public int Percent { get; set; }
}
