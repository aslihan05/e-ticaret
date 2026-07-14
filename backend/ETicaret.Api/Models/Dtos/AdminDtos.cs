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

public class OrderStatusDto
{
    public OrderStatus Status { get; set; }
}

// Kalem bazlı karar: listedekiler onaylanır, listede olmayan kalemler reddedilir
public class OrderDecisionDto
{
    public List<int> ApprovedItemIds { get; set; } = new();
}

// Admin'in kullanıcı ekleme/güncelleme formu; güncellemede boş bırakılan alanlar değişmez.
public class UserUpsertDto
{
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? Role { get; set; }
}
