using ETicaret.Api.Models.Entities;

namespace ETicaret.Api.Models.Dtos;

// Müşterinin sepette "Uygula" derken gönderdiği gövde.
// Sepet tutarı GÖNDERİLMEZ; sunucu onu kullanıcının gerçek sepetinden hesaplar.
public class ApplyCouponDto
{
    public string Code { get; set; } = string.Empty;
}

// Admin'in kupon oluştururken/güncellerken gönderdiği gövde.
public class CouponUpsertDto
{
    public string Code { get; set; } = string.Empty;
    public CouponType Type { get; set; }
    public decimal Value { get; set; }

    // Kupon kimlere tanımlanıyor? Boş/null = herkese açık kampanya.
    public List<int>? UserIds { get; set; }

    public decimal? MinOrderTotal { get; set; }
    public int? MaxUses { get; set; }
    public int? PerUserLimit { get; set; }
    public DateTime? StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }
    public bool IsActive { get; set; } = true;
}
