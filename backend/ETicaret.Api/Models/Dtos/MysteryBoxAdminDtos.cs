using ETicaret.Api.Models.Entities;

namespace ETicaret.Api.Models.Dtos;

// Gizemli hediye kutusunun YÖNETİM tipleri. Müşteriye dönen tipler MysteryBoxDtos.cs'te;
// buradakiler yalnızca admin panelinin gördüğü ayar + ödül havuzu görünümüdür.

// Tek bir ödülün yönetim satırı. Chance, adminin "bu ödül ne sıklıkta çıkar?" sorusunu
// ağırlıkları kafasında oranlamadan görebilmesi için sunucuda hesaplanır.
public class MysteryPrizeAdminDto
{
    public int Id { get; set; }
    public string Emoji { get; set; } = "";
    public string Label { get; set; } = "";
    public CouponType Type { get; set; }
    public decimal Value { get; set; }
    public decimal? MinOrderTotal { get; set; }
    public int Weight { get; set; }
    public bool IsActive { get; set; }
    public int SortOrder { get; set; }

    // Aktif ödüller içindeki çıkma ihtimali (%). Kapalı ödüllerde 0.
    public double Chance { get; set; }

    // Bu ödülün şimdiye dek kaç kez kazanıldığı (üretilmiş kuponlardan sayılır).
    public int WonCount { get; set; }
}

// GET /api/mysterybox/admin cevabı: ayarlar + havuzun tamamı (kapalılar dahil).
public class MysteryBoxAdminDto
{
    public bool IsActive { get; set; }
    public int CooldownHours { get; set; }
    public int CouponValidDays { get; set; }
    public int BoxCount { get; set; }
    public List<MysteryPrizeAdminDto> Prizes { get; set; } = new();

    // Toplam kaç kutu açıldığı (üretilen hediye kuponu sayısı) — ayarların etkisini görmek için.
    public int TotalPlays { get; set; }
}

// Ayar güncelleme.
public class MysteryBoxSettingsDto
{
    public bool IsActive { get; set; }
    public int CooldownHours { get; set; }
    public int CouponValidDays { get; set; }
    public int BoxCount { get; set; }
}

// Ödül ekleme/güncelleme. Id null ya da 0 ise yeni ödül eklenir.
public class MysteryPrizeUpsertDto
{
    public int? Id { get; set; }
    public string Emoji { get; set; } = "🎁";
    public string Label { get; set; } = "";
    public CouponType Type { get; set; }
    public decimal Value { get; set; }
    public decimal? MinOrderTotal { get; set; }
    public int Weight { get; set; } = 10;
    public bool IsActive { get; set; } = true;
    public int? SortOrder { get; set; }
}
