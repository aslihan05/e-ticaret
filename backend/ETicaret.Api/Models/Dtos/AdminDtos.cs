using ETicaret.Api.Models.Entities;

namespace ETicaret.Api.Models.Dtos;

public class StockUpdateDto
{
    public int Stock { get; set; }
}

public class DiscountDto
{
    public decimal? DiscountPrice { get; set; }
    public DateTime? DiscountStart { get; set; }
    public DateTime? DiscountEnd { get; set; }
}

// Bir kategorideki tüm aktif ürünlere yüzde oranıyla indirim uygulamak için
public class CategoryDiscountDto
{
    public int Percent { get; set; }              // 1-99 arası indirim yüzdesi
    public DateTime? DiscountStart { get; set; }
    public DateTime? DiscountEnd { get; set; }
}

// Seçilen (farklı kategorilerden olabilir) ürünlere topluca indirim.
// Percent null/0 ise seçilenlerin indirimi kaldırılır.
public class BulkDiscountDto
{
    public List<int> Ids { get; set; } = new();
    public int? Percent { get; set; }
    public DateTime? DiscountStart { get; set; }
    public DateTime? DiscountEnd { get; set; }
}

public class OrderStatusDto
{
    public OrderStatus Status { get; set; }
}

// Kalem bazlı karar: yalnızca listelerde adı geçen kalemler karara bağlanır.
// Hiçbir listede olmayan kalem BEKLEMEDE kalır — admin aynı siparişin bir ürününü
// onaylayıp diğerini sonraya bırakabilsin diye (eskiden seçilmeyen kalem reddediliyordu).
public class OrderDecisionDto
{
    public List<int> ApprovedItemIds { get; set; } = new();
    public List<int> RejectedItemIds { get; set; } = new();
}

// Admin'in kullanıcı ekleme/güncelleme formu; güncellemede boş/null bırakılan alanlar değişmez.
public class UserUpsertDto
{
    public string? Username { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? Password { get; set; }
    public string? Role { get; set; }
    public int? SortOrder { get; set; }   // Kullanıcılar listesindeki manuel sıra
    public bool? IsBlocked { get; set; }  // true/false gelirse güncellenir, null gelirse dokunulmaz
}
