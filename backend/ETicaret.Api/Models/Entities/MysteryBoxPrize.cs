namespace ETicaret.Api.Models.Entities;

// Gizemli hediye kutusunun ödül havuzundaki TEK bir ödül. Önceden bu havuz koda gömülüydü
// (sabit dizi); artık yönetilebilir olsun diye veritabanında tutuluyor: admin ödül ekler,
// oranını değiştirir, çıkma ihtimalini (Weight) ayarlar ya da geçici olarak kapatır.
//
// Ödülün kendisi hâlâ SUNUCUDA seçilir ve gerçek bir kupona dönüşür; bu tablo yalnızca
// "hangi ödüller, hangi ihtimalle" sorusunu yanıtlar.
public class MysteryBoxPrize
{
    public int Id { get; set; }

    // Kutu açılınca gösterilen görsel/başlık (ör. "🎟️", "%10 İndirim")
    public string Emoji { get; set; } = "🎁";
    public string Label { get; set; } = string.Empty;

    // Kazanılınca üretilecek kuponun tipi ve değeri (yüzde ise 1-99, tutar ise TL).
    public CouponType Type { get; set; }
    public decimal Value { get; set; }

    // Varsa kuponun alt sepet limiti (null = limitsiz)
    public decimal? MinOrderTotal { get; set; }

    // Çıkma ağırlığı: 1 = nadir, 10 = sık. Seçim ağırlıklı rastgeledir, yani bir ödülün
    // çıkma ihtimali kendi ağırlığının aktif ödüllerin ağırlık toplamına oranıdır.
    // (Eşit ağırlık verilirse davranış eski "tamamen rastgele" havuzla aynıdır.)
    public int Weight { get; set; } = 10;

    // Kapalı ödül havuza hiç girmez; silmek yerine kapatmak, o ödülü kazanmış geçmiş
    // kuponların anlamını korur (ürün/kupon soft-delete mantığının aynısı).
    public bool IsActive { get; set; } = true;

    // Admin tablosundaki görüntüleme sırası
    public int SortOrder { get; set; }
}
