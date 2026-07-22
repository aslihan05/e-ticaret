using ETicaret.Api.Data;
using ETicaret.Api.Models.Dtos;
using ETicaret.Api.Models.Entities;
using ETicaret.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ETicaret.Api.Controllers;

// Gizemli Hediye Kutuları — oyunlaştırma modülü.
//
// TASARIM İLKESİ (client-side hilesine kapalı): Müşteri ekranda 3 kutudan birine tıklar,
// ama HANGİ ödülü kazandığı tarayıcıda değil BURADA belirlenir. Kazanılan ödül gerçek bir
// kupona dönüşür, kullanıcıya atanır ve "Kuponlarım"da görünür. İstemci isterse isteği
// tekrar tekrar göndersin — günde bir kez kuralı da sunucuda zorlanır, o yüzden hile
// (ör. daha iyi ödül için sürekli deneme) işe yaramaz.
//
// Neden ayrı bir "MysteryBoxOpening" tablosu/migration YOK: Her kazanç zaten Coupons
// tablosuna, koda özel önekle (HEDIYE...) ve o kullanıcıya atanmış olarak yazılıyor.
// "Bugün oynadı mı" sorusunun cevabı bu kayıtların en yenisinin CreatedAt'inden çıkar —
// ikinci bir doğruluk kaynağı (ve şema değişikliği) eklemeye gerek kalmaz.
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class MysteryBoxController : ControllerBase
{
    private readonly AppDbContext _context;

    public MysteryBoxController(AppDbContext context)
    {
        _context = context;
    }

    private int CurrentUserId => int.Parse(User.FindFirst("UserId")!.Value);

    // Hediye kuponlarının kod öneki: hem çakışmasız üretim hem de "bu bir hediye kutusu
    // kuponu mu" ayrımı için. Admin panelinden oluşturulan normal kuponlar bu öneki taşımaz.
    private const string CodePrefix = "HEDIYE";

    // Günde bir kez oynanabilir. 24 saat, deneme-yanılmayla daha iyi ödül avlamayı engeller.
    private const int CooldownHours = 24;

    // Kazanılan kuponun geçerlilik süresi. Süresiz bırakmak, oynanan her gün için
    // biriken ölü kupon yığını demekti.
    private const int CouponValidDays = 7;

    // ===== Ödül havuzu (yalnızca sunucuda) =====
    // (Type, Value) çiftleri BENZERSİZ tutulur: "bugün ne kazandın" bilgisini kupondan
    // geri okurken ödülü bu çiftle eşleştiriyoruz.
    private record Prize(string Emoji, string Label, CouponType Type, decimal Value, decimal? Min);

    // Not: "Kargo Bedava" havuzdan çıkarıldı — sistemde kargo ücreti kavramı yok
    // (kargo yalnızca bir sipariş DURUMU), o yüzden "bedava kargo" ödülü müşteriye
    // karşılığı olmayan bir vaat olurdu. Yerine dürüst ve işlevsel bir tutar indirimi
    // kondu; ödüller net bir merdiven oluşturuyor: %10/15/20/25 ve 30/40/50 TL.
    private static readonly Prize[] Pool =
    {
        new("🎟️", "%10 İndirim",   CouponType.Percent, 10, null),
        new("🎫", "%15 İndirim",   CouponType.Percent, 15, null),
        new("🎉", "%20 İndirim",   CouponType.Percent, 20, 150),
        new("💎", "%25 İndirim",   CouponType.Percent, 25, 300),
        new("💰", "30 TL İndirim", CouponType.Amount,  30, 150),
        new("🎁", "40 TL İndirim", CouponType.Amount,  40, 200),
        new("🤑", "50 TL İndirim", CouponType.Amount,  50, 400),
    };

    // Sayfa açılışı: kutular açılabilir mi, yoksa "yarın tekrar gel" mi?
    [HttpGet]
    public async Task<ActionResult<MysteryBoxStatusDto>> Status()
    {
        var son = await SonHediyeKuponu(CurrentUserId);
        var now = DateTime.UtcNow;

        if (son != null && son.CreatedAt.AddHours(CooldownHours) > now)
        {
            return Ok(new MysteryBoxStatusDto
            {
                CanOpen = false,
                NextAvailableAt = son.CreatedAt.AddHours(CooldownHours),
                LastPrize = PrizeToDto(EslesenOdul(son), son)   // bugün kazanılanı göster
            });
        }

        return Ok(new MysteryBoxStatusDto { CanOpen = true });
    }

    // Kutuya tıklama: ödülü belirler, kuponu oluşturur, kazanılan + kaçırılan 2 ödülü döner.
    [HttpPost("open")]
    public async Task<ActionResult<MysteryBoxResultDto>> Open()
    {
        int userId = CurrentUserId;
        var now = DateTime.UtcNow;

        // 1) Cooldown — sunucuda zorlanır. İstemci ne kadar denerse denesin geçemez.
        var son = await SonHediyeKuponu(userId);
        if (son != null && son.CreatedAt.AddHours(CooldownHours) > now)
        {
            return Ok(new MysteryBoxResultDto
            {
                AlreadyPlayed = true,
                Won = PrizeToDto(EslesenOdul(son), son),
                NextAvailableAt = son.CreatedAt.AddHours(CooldownHours),
                Message = "Bugünkü hediye kutunu zaten açtın. Yeni kutu için yarın tekrar gel! 🎁"
            });
        }

        // 2) Havuzdan 3 farklı ödül seç, kazananı SUNUCUDA rastgele belirle.
        var secilenler = Pool.OrderBy(_ => Random.Shared.Next()).Take(3).ToList();
        int kazananIndex = Random.Shared.Next(secilenler.Count);
        var kazanan = secilenler[kazananIndex];

        // 3) Kazanılan ödülü gerçek, kişiye özel, tek kullanımlık kupona dönüştür.
        var coupon = new Coupon
        {
            Code = await BenzersizKodUret(),
            Type = kazanan.Type,
            Value = kazanan.Value,
            MinOrderTotal = kazanan.Min,
            MaxUses = 1,           // tek sipariş
            PerUserLimit = 1,      // ve o kişi için tek kez
            StartsAt = now,
            EndsAt = now.AddDays(CouponValidDays),
            IsActive = true,
            CreatedAt = now
        };
        coupon.AssignedUsers.Add(new CouponUser { UserId = userId });   // yalnızca bu müşteriye
        _context.Coupons.Add(coupon);

        _context.Logs.Add(new Log
        {
            UserId = userId,
            Action = "Hediye kutusu açıldı",
            Details = $"{kazanan.Label} kazanıldı ({coupon.Code})",
            Timestamp = now
        });

        await _context.SaveChangesAsync();

        // 4) Kazanılan (kodlu) + kaçırılan 2 ödül (kodsuz — kullanılamaz).
        var kacirilanlar = secilenler
            .Where((_, i) => i != kazananIndex)
            .Select(p => PrizeToDto(p, null))
            .ToList();

        return Ok(new MysteryBoxResultDto
        {
            AlreadyPlayed = false,
            Won = PrizeToDto(kazanan, coupon),
            Missed = kacirilanlar,
            NextAvailableAt = now.AddHours(CooldownHours),
            Message = $"Tebrikler! {kazanan.Emoji} {kazanan.Label} kazandın. Kupon kodun: {coupon.Code}"
        });
    }

    // Bu kullanıcıya atanmış en yeni hediye-kutusu kuponu (yoksa null).
    private Task<Coupon?> SonHediyeKuponu(int userId) =>
        _context.Coupons
            .Where(c => c.Code.StartsWith(CodePrefix)
                        && c.AssignedUsers.Any(a => a.UserId == userId))
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync();

    // Kupondaki (Type, Value) ikilisini havuzdaki ödüle geri eşler; bulunamazsa
    // (havuz sonradan değişmişse) kupondan türetilmiş yedek bir ödül döner.
    private static Prize EslesenOdul(Coupon c) =>
        Pool.FirstOrDefault(p => p.Type == c.Type && p.Value == c.Value)
        ?? new Prize("🎁", CouponService.Ozet(c.Type, c.Value) + " İndirim", c.Type, c.Value, c.MinOrderTotal);

    // Ödülü DTO'ya çevirir. coupon verilirse (kazanılan ödül) kod ve son kullanma dolar;
    // null ise (kaçırılan ödül) bu alanlar boş kalır — kullanılamaz.
    private static MysteryPrizeDto PrizeToDto(Prize p, Coupon? coupon) => new()
    {
        Emoji = p.Emoji,
        Label = p.Label,
        Summary = CouponService.Ozet(p.Type, p.Value),
        MinOrderTotal = p.Min,
        Code = coupon?.Code,
        ExpiresAt = coupon?.EndsAt
    };

    // Çakışmayan bir kupon kodu üretir (HEDIYE + 5 karakter). Karışması kolay
    // harf/rakamlar (0/O, 1/I) havuz dışında bırakıldı.
    private async Task<string> BenzersizKodUret()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        for (int deneme = 0; deneme < 10; deneme++)
        {
            var kod = CodePrefix + new string(Enumerable.Range(0, 5)
                .Select(_ => chars[Random.Shared.Next(chars.Length)]).ToArray());
            if (!await _context.Coupons.AnyAsync(c => c.Code == kod))
            {
                return kod;
            }
        }
        // Son çare: zaman damgalı (pratikte buraya düşülmez).
        return CodePrefix + DateTime.UtcNow.Ticks.ToString().Substring(10);
    }
}
