using System.Collections.Generic;                 namespace ETicaret.Api.Models.Entities;

public enum OrderStatus    // enum geçerli değerleri derleyiciye denetletir
{
    Pending,  // OrderStatus.Pending ile her sipariş taslaktaki kurala uygun olarak "onay bekliyor" doğar.
    Approved,
    Rejected,
    Shipped,   // Kargoya verildi
    Delivered, // Teslim edildi
    Cancelled  // Müşteri iptal etti (sadece Bekliyor durumundayken)
}

public class Order
{
    public int Id { get; set; }
    public int UserId { get; set; }  // Sipariş veren
    public User User {get; set;} = null!;

    public OrderStatus Status {get; set;} = OrderStatus.Pending;

    public DateTime CreatedAt {get; set; }
    public DateTime? ApprovedAt {get; set;}

    public int? ApprovedBy {get; set;}     // int? -> Çünkü henüz onaylanmamış siparişin onaylayanı yoktur.
    public User? ApprovedByUser {get; set;}  // Onaylayan admin

    // Teslimat bilgileri: sipariş anındaki anlık görüntü (adres sonradan değişse bile sipariş sabit kalır).
    // Eski siparişlerde boş olabileceği için nullable.
    public string? RecipientName { get; set; }  // Alıcı ad-soyad
    public string? Phone { get; set; }           // İletişim telefonu
    public string? City { get; set; }            // İl (şehir)
    public string? District { get; set; }        // İlçe
    public string? Address { get; set; }         // Açık adres

    public ICollection<OrderItem> OrderItems { get; set; } = new   // Listenin birçok sipariş satırı olur.
    List<OrderItem>();  // Boşaltıyorurz ki checkout'ta order.OrderItems.Add(...) derken null hatası almayalım.

    // ===== Kupon =====
    // CouponId yalnızca raporlama/kullanım sayımı içindir. İndirimin ŞARTLARI aşağıya
    // ayrıca kopyalanır: admin kuponun oranını sonradan değiştirirse ya da kuponu
    // kapatırsa, geçmiş siparişin indirimi değişmemelidir. (UnitPrice'ı sipariş anında
    // dondurmakla aynı gerekçe.)
    public int? CouponId { get; set; }
    public Coupon? Coupon { get; set; }

    public string? CouponCode { get; set; }            // Gösterim için
    public CouponType? CouponType { get; set; }        // Şart kopyası
    public decimal? CouponValue { get; set; }          // Şart kopyası
    public decimal? CouponMinOrderTotal { get; set; }  // Şart kopyası

    // İndirim TUTARI saklanmaz, her seferinde gerçek kalemler üzerinden hesaplanır.
    // Sebebi kısmi onay: admin siparişin bir kısmını reddedebiliyor. Tutar dondurulsaydı,
    // 1000 TL'lik sepete verilen %10'luk 100 TL indirim, 900 TL'lik kalem reddedilince
    // kalan 100 TL'nin tamamını silerdi. Şartlar sabit, taban değişken.
    public decimal CalculateDiscount(decimal subtotal)
    {
        if (CouponType == null || CouponValue == null) return 0;

        // Alt limit, siparişin GERÇEKLEŞEN tutarına göre kontrol edilir: kalemler
        // reddedildikten sonra sepet limitin altına düştüyse kuponun şartı sağlanmıyordur.
        if (subtotal < (CouponMinOrderTotal ?? 0)) return 0;

        var indirim = CouponType == Entities.CouponType.Percent
            ? Math.Round(subtotal * CouponValue.Value / 100m, 2)
            : CouponValue.Value;

        // İndirim hiçbir koşulda sepetten büyük olamaz — negatif toplam anlamsızdır.
        return Math.Min(indirim, subtotal);
    }
}