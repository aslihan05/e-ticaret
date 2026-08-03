using ETicaret.Api.Data;
using ETicaret.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace ETicaret.Api.Services;

// Kupon geçersizse fırlatılır. Mesajı doğrudan müşteriye gösterilebilir
// (Türkçe ve nedeni açıklayan bir metin taşır).
public class CouponException : Exception
{
    public CouponException(string message) : base(message) { }
}

// Kupon doğrulama ve indirim hesabı TEK yerde.
//
// Aynı kural iki yerde çalışıyor: sepetteki "Uygula" önizlemesi ve asıl checkout.
// İkisi ayrı yazılsaydı, sepette geçerli görünüp sipariş anında reddedilen (ya da tersi)
// bir kupon kaçınılmaz olurdu. Önizleme de sipariş de bu servisten geçer.
public class CouponService
{
    private readonly AppDbContext _context;

    public CouponService(AppDbContext context)
    {
        _context = context;
    }

    // Kupon değerinin okunabilir hali: 10.00 -> "10", 12.50 -> "12,5".
    // Ham decimal basıldığında "%10,00 indirim" gibi sondaki sıfırlar görünüyordu.
    public static string Bicimle(decimal value) => value.ToString("0.##");

    // Kuponun insan diliyle özeti: "%10" ya da "50 TL".
    public static string Ozet(CouponType type, decimal value) =>
        type == CouponType.Percent ? $"%{Bicimle(value)}" : $"{Bicimle(value)} TL";

    // Kod hep büyük harf ve boşluksuz: müşteri "yaz10 " yazdığında da bulunmalı.
    // Türkçe'ye özgü tuzak: "i".ToUpper() Türkçe kültürde "İ" verir ve veritabanındaki
    // "I" ile eşleşmez. Kod alfanumerik olduğu için karşılaştırma kültürden bağımsız yapılır.
    public static string Normalize(string? code) =>
        (code ?? string.Empty).Trim().ToUpperInvariant();

    // Kuponun bu kullanıcı ve bu sepet tutarı için geçerli olup olmadığını denetler.
    // Geçerliyse kuponu döndürür; değilse nedenini söyleyen CouponException fırlatır.
    public async Task<Coupon> ValidateAsync(string? code, int userId, decimal subtotal)
    {
        var kod = Normalize(code);

        if (kod.Length == 0)
        {
            throw new CouponException("Kupon kodu girmelisiniz.");
        }

        var coupon = await _context.Coupons
            .Include(c => c.AssignedUsers)
            .FirstOrDefaultAsync(c => c.Code == kod);

        // Kapalı kupon ile var olmayan kupon aynı mesajı alır: "bu kod kapalı" demek,
        // deneme yanılmayla geçerli kod listesi çıkarmayı kolaylaştırırdı.
        //
        // Başkalarına tanımlanmış kupon da AYNI mesajı alır. "Bu kupon size tanımlı değil"
        // demek, kodun var olduğunu doğrulardı; müşteri için ikisi de kullanılamaz koddur.
        bool banaTanimliDegil = coupon != null
            && coupon.AssignedUsers.Count > 0
            && !coupon.AssignedUsers.Any(a => a.UserId == userId);

        if (coupon == null || !coupon.IsActive || banaTanimliDegil)
        {
            throw new CouponException("Kupon kodu geçersiz.");
        }

        var now = DateTime.UtcNow;

        if (coupon.StartsAt != null && coupon.StartsAt > now)
        {
            throw new CouponException("Bu kupon henüz başlamadı.");
        }

        if (coupon.EndsAt != null && coupon.EndsAt < now)
        {
            throw new CouponException("Bu kuponun süresi dolmuş.");
        }

        if (coupon.MinOrderTotal != null && subtotal < coupon.MinOrderTotal)
        {
            throw new CouponException($"Bu kupon en az {Bicimle(coupon.MinOrderTotal.Value)} TL'lik sepetlerde geçerli.");
        }

        // Kullanım sayısı Orders'tan sayılır (kuponda sayaç yok). İptal edilen ya da
        // reddedilen sipariş kullanım saymaz — o alışveriş hiç gerçekleşmedi.
        if (coupon.MaxUses != null)
        {
            int toplamKullanim = await KullanimSayisiAsync(coupon.Id, null);
            if (toplamKullanim >= coupon.MaxUses)
            {
                throw new CouponException("Bu kuponun kullanım limiti dolmuş.");
            }
        }

        if (coupon.PerUserLimit != null)
        {
            int kendiKullanimi = await KullanimSayisiAsync(coupon.Id, userId);
            if (kendiKullanimi >= coupon.PerUserLimit)
            {
                throw new CouponException(coupon.PerUserLimit == 1
                    ? "Bu kuponu zaten kullandınız."
                    : $"Bu kuponu en fazla {coupon.PerUserLimit} kez kullanabilirsiniz.");
            }
        }

        return coupon;
    }

    // userId null ise kuponun toplam kullanımı, doluysa o kullanıcınınki sayılır.
    //
    // Sayım SİPARİŞ değil ALIŞVERİŞ (CheckoutId) bazındadır: sepetteki her ürün ayrı bir
    // sipariş doğurduğu için, 3 ürün alan müşteri kuponu tek kullandığı hâlde 3 kullanım
    // görünürdü — tek kullanımlık kupon ilk alışverişte limitini doldururdu.
    private Task<int> KullanimSayisiAsync(int couponId, int? userId) =>
        _context.Orders
            .Where(o => o.CouponId == couponId
                && (userId == null || o.UserId == userId)
                && o.Status != OrderStatus.Cancelled
                && o.Status != OrderStatus.Rejected)
            .Select(o => o.CheckoutId)
            .Distinct()
            .CountAsync();

    // Kuponun şartlarını siparişe kopyalar. İndirim tutarı yazılmaz; sipariş onu
    // kendi kalemlerinden hesaplar (bkz. Order.CalculateDiscount).
    public static void Uygula(Order order, Coupon coupon)
    {
        order.CouponId = coupon.Id;
        order.CouponCode = coupon.Code;
        order.CouponType = coupon.Type;
        order.CouponValue = coupon.Value;
        order.CouponMinOrderTotal = coupon.MinOrderTotal;
    }

    // Kuponun kaç siparişte kullanıldığı — admin listesinde göstermek için.
    public Task<int> ToplamKullanimAsync(int couponId) => KullanimSayisiAsync(couponId, null);
}
