namespace ETicaret.Api.Models.Entities;

// Bir kuponun hangi müşterilere tanımlandığını tutan ara tablo (kupon ↔ kullanıcı).
//
// Bir kupon için HİÇ satır yoksa kupon herkese açıktır; satır varsa yalnızca o satırlardaki
// müşteriler kuponu görebilir ve kullanabilir.
//
// Kaç kez kullanıldığı burada tutulmaz — o bilgi Orders üzerinden sayılır (bkz. Coupon).
// Buradaki satır "kullanma hakkı"dır, "kullandı" değil.
public class CouponUser
{
    public int CouponId { get; set; }
    public Coupon Coupon { get; set; } = null!;

    public int UserId { get; set; }
    public User User { get; set; } = null!;
}
