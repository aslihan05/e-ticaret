namespace ETicaret.Api.Models.Entities;

// "Haftanın Fırsatı" bölümünün tekil (singleton) ayar kaydı: tüm bölüm için ortak başlık,
// geri sayım bitiş tarihi ve aktiflik. Uygulamada bu tablonun tek bir satırı (Id = 1) olur;
// hangi ürünlerin vitrinde olduğu Product.WeeklyDealOrder ile, indirimleri ise ürünlerin
// kendi indirim alanlarıyla (DiscountPrice/Start/End) tutulur.
public class WeeklyDealConfig
{
    public int Id { get; set; }

    public string Title { get; set; } = "Haftanın Fırsatı";

    // Kampanyanın başlayacağı an (UTC). null ise "hemen geçerli". Doluysa ve henüz
    // gelmediyse bölüm ziyaretçiye GÖSTERİLMEZ ve vitrindeki ürünlerin indirimi de
    // başlamaz (DiscountStart bu tarihe eşitlenir) — admin kampanyayı önceden hazırlayıp
    // gününde kendiliğinden açılmasını sağlayabilsin diye.
    public DateTime? StartsAt { get; set; }

    // Geri sayımın bittiği an (UTC). null ise sayaç gösterilmez ve vitrindeki ürünlerin
    // indirimleri süresiz kalır. Ayar güncellenince vitrindeki tüm ürünlerin DiscountEnd'i
    // bu tarihe eşitlenir — böylece sayaç bitince indirim otomatik olarak düşer.
    public DateTime? EndsAt { get; set; }

    // false ise bölüm ziyaretçiye hiç gösterilmez (ürünlerin indirimi ayrıca yönetilebilir).
    public bool IsActive { get; set; } = true;
}
