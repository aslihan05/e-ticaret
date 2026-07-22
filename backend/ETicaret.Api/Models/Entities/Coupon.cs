namespace ETicaret.Api.Models.Entities;

public enum CouponType
{
    Percent,   // Yüzde indirim: Value = 10 -> %10
    Amount     // Tutar indirimi: Value = 50 -> 50 TL
}

public class Coupon
{
    public int Id { get; set; }

    // Kullanıcının yazdığı kod. Her zaman BÜYÜK harfe çevrilerek saklanır ve aranır;
    // müşteri "yaz10" yazdığında "YAZ10" kuponunu bulamamak kabul edilebilir bir davranış değil.
    public string Code { get; set; } = string.Empty;

    public CouponType Type { get; set; }

    // Yüzde ise 1-99, tutar ise TL. Anlamı Type'a bağlı olduğu için tek alan yeterli.
    public decimal Value { get; set; }

    // Bu tutarın altındaki sepetlerde kupon geçmez (null = alt limit yok)
    public decimal? MinOrderTotal { get; set; }

    // Kuponun tanımlandığı müşteriler. BOŞ = herkese açık kampanya.
    // Doluysa yalnızca listedeki müşteriler kuponu görebilir ve kullanabilir.
    //
    // Ara tablo, çünkü bir kupon birden çok kişiye tanımlanabiliyor. Önceden Coupon üzerinde
    // tek bir UserId vardı; "bu kuponu 5 müşteriye tanımla" istendiğinde aynı kuponu 5 kez
    // farklı kodlarla açmak gerekiyordu.
    public ICollection<CouponUser> AssignedUsers { get; set; } = new List<CouponUser>();

    // Kuponun toplam kaç siparişte kullanılabileceği (null = sınırsız)
    public int? MaxUses { get; set; }

    // Tek bir kullanıcının bu kuponu kaç kez kullanabileceği (null = sınırsız)
    public int? PerUserLimit { get; set; }

    // Geçerlilik aralığı; ikisi de null olabilir (süresiz kupon)
    public DateTime? StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }

    // Kampanyayı silmeden kapatabilmek için. Silmek, kuponu kullanmış siparişlerin
    // bağlantısını koparırdı; ürünlerdeki soft-delete mantığının aynısı.
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }

    // Kaç kez kullanıldığı BURADA bir sayaç olarak tutulmaz; Orders üzerinden sayılır.
    // Ayrı bir sayaç, sipariş iptal/red edildiğinde ya da elle veri düzeltildiğinde
    // gerçekle arasında sessizce fark oluşabilecek ikinci bir doğruluk kaynağı olurdu.
    public ICollection<Order> Orders { get; set; } = new List<Order>();
}
