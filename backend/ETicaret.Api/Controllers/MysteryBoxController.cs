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
// TASARIM İLKESİ (client-side hilesine kapalı): Müşteri ekranda kutulardan birine tıklar,
// ama HANGİ ödülü kazandığı tarayıcıda değil BURADA belirlenir. Kazanılan ödül gerçek bir
// kupona dönüşür, kullanıcıya atanır ve "Kuponlarım"da görünür. İstemci isterse isteği
// tekrar tekrar göndersin — bekleme süresi kuralı da sunucuda zorlanır, o yüzden hile
// (ör. daha iyi ödül için sürekli deneme) işe yaramaz.
//
// ÖDÜL HAVUZU ARTIK YÖNETİLEBİLİR: Ödüller ve çıkma ağırlıkları koda gömülü değil,
// MysteryBoxPrizes tablosunda; bekleme süresi/geçerlilik gibi ayarlar MysteryBoxConfigs'te.
// Admin paneli (…/admin uçları) bu iki tabloyu yönetir. Tablo boşsa aşağıdaki VarsayilanHavuz
// bir kereye mahsus tohumlanır — yani sistem ayar yapılmadan da çalışır durumda başlar.
//
// Neden ayrı bir "MysteryBoxOpening" tablosu YOK: Her kazanç zaten Coupons tablosuna,
// koda özel önekle (HEDIYE...) ve o kullanıcıya atanmış olarak yazılıyor. "Bugün oynadı mı"
// sorusunun cevabı bu kayıtların en yenisinin CreatedAt'inden çıkar — ikinci bir doğruluk
// kaynağı eklemeye gerek kalmaz.
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

    // ===== İlk kurulum havuzu =====
    // Yalnızca tablo boşken bir kez veritabanına yazılır; sonrasında tek doğruluk kaynağı
    // tablonun kendisidir. Not: "Kargo Bedava" bilerek yok — sistemde kargo ücreti kavramı
    // olmadığı için müşteriye karşılığı olmayan bir vaat olurdu. Ödüller net bir merdiven
    // oluşturur: %10/15/20/25 ve 30/40/50 TL; nadir ödüller düşük ağırlıkla.
    private static readonly MysteryBoxPrize[] VarsayilanHavuz =
    {
        new() { Emoji = "🎟️", Label = "%10 İndirim",   Type = CouponType.Percent, Value = 10, MinOrderTotal = null, Weight = 25, SortOrder = 1 },
        new() { Emoji = "🎫", Label = "%15 İndirim",   Type = CouponType.Percent, Value = 15, MinOrderTotal = null, Weight = 20, SortOrder = 2 },
        new() { Emoji = "🎉", Label = "%20 İndirim",   Type = CouponType.Percent, Value = 20, MinOrderTotal = 150,  Weight = 15, SortOrder = 3 },
        new() { Emoji = "💎", Label = "%25 İndirim",   Type = CouponType.Percent, Value = 25, MinOrderTotal = 300,  Weight = 5,  SortOrder = 4 },
        new() { Emoji = "💰", Label = "30 TL İndirim", Type = CouponType.Amount,  Value = 30, MinOrderTotal = 150,  Weight = 15, SortOrder = 5 },
        new() { Emoji = "🎁", Label = "40 TL İndirim", Type = CouponType.Amount,  Value = 40, MinOrderTotal = 200,  Weight = 12, SortOrder = 6 },
        new() { Emoji = "🤑", Label = "50 TL İndirim", Type = CouponType.Amount,  Value = 50, MinOrderTotal = 400,  Weight = 8,  SortOrder = 7 },
    };

    // Tekil ayar satırını getirir; yoksa varsayılanla oluşturur (WeeklyDeals ile aynı desen).
    private async Task<MysteryBoxConfig> AyarGetirVeyaOlustur()
    {
        var config = await _context.MysteryBoxConfigs.FirstOrDefaultAsync();
        if (config == null)
        {
            config = new MysteryBoxConfig();
            _context.MysteryBoxConfigs.Add(config);
            await _context.SaveChangesAsync();
        }
        return config;
    }

    // Havuzu getirir; tablo hiç doldurulmamışsa varsayılan ödülleri bir kez tohumlar.
    private async Task<List<MysteryBoxPrize>> HavuzGetirVeyaTohumla()
    {
        var prizes = await _context.MysteryBoxPrizes.OrderBy(p => p.SortOrder).ToListAsync();
        if (prizes.Count == 0)
        {
            prizes = VarsayilanHavuz.Select(p => new MysteryBoxPrize
            {
                Emoji = p.Emoji, Label = p.Label, Type = p.Type, Value = p.Value,
                MinOrderTotal = p.MinOrderTotal, Weight = p.Weight, IsActive = true, SortOrder = p.SortOrder
            }).ToList();
            _context.MysteryBoxPrizes.AddRange(prizes);
            await _context.SaveChangesAsync();
        }
        return prizes;
    }

    // Ağırlıklı rastgele seçim: bir ödülün çıkma ihtimali kendi ağırlığının toplam ağırlığa
    // oranıdır. Tüm ağırlıklar 0/negatifse eşit dağılıma düşülür (ayar hatası oyunu kilitlemesin).
    private static MysteryBoxPrize AgirlikliSec(List<MysteryBoxPrize> havuz)
    {
        int toplam = havuz.Sum(p => Math.Max(0, p.Weight));
        if (toplam <= 0)
        {
            return havuz[Random.Shared.Next(havuz.Count)];
        }

        int nokta = Random.Shared.Next(toplam);
        foreach (var p in havuz)
        {
            nokta -= Math.Max(0, p.Weight);
            if (nokta < 0) return p;
        }
        return havuz[^1];
    }

    // Sayfa açılışı: kutular açılabilir mi, "yarın tekrar gel" mi, yoksa oyun kapalı mı?
    [HttpGet]
    public async Task<ActionResult<MysteryBoxStatusDto>> Status()
    {
        var config = await AyarGetirVeyaOlustur();
        var aktifHavuz = (await HavuzGetirVeyaTohumla()).Where(p => p.IsActive).ToList();

        // Oyun kapalıysa ya da havuzda hiç ödül kalmadıysa kutular gösterilmez.
        if (!config.IsActive || aktifHavuz.Count == 0)
        {
            return Ok(new MysteryBoxStatusDto { CanOpen = false, GameClosed = true });
        }

        var son = await SonHediyeKuponu(CurrentUserId);
        var now = DateTime.UtcNow;

        if (son != null && son.CreatedAt.AddHours(config.CooldownHours) > now)
        {
            return Ok(new MysteryBoxStatusDto
            {
                CanOpen = false,
                NextAvailableAt = son.CreatedAt.AddHours(config.CooldownHours),
                LastPrize = PrizeToDto(EslesenOdul(await HavuzGetirVeyaTohumla(), son), son),  // bugün kazanılanı göster
                BoxCount = KutuSayisi(config, aktifHavuz.Count)
            });
        }

        return Ok(new MysteryBoxStatusDto { CanOpen = true, BoxCount = KutuSayisi(config, aktifHavuz.Count) });
    }

    // Ekrandaki kutu sayısı havuzdaki aktif ödül sayısını aşamaz (aksi halde aynı ödül
    // iki kutuda görünürdü); en az 1 kutu olur.
    private static int KutuSayisi(MysteryBoxConfig config, int aktifOdulSayisi) =>
        Math.Max(1, Math.Min(config.BoxCount, aktifOdulSayisi));

    // Kutuya tıklama: ödülü belirler, kuponu oluşturur, kazanılan + kaçırılan ödülleri döner.
    [HttpPost("open")]
    public async Task<ActionResult<MysteryBoxResultDto>> Open()
    {
        int userId = CurrentUserId;
        var now = DateTime.UtcNow;

        var config = await AyarGetirVeyaOlustur();
        var havuz = await HavuzGetirVeyaTohumla();
        var aktifHavuz = havuz.Where(p => p.IsActive).ToList();

        // 0) Oyun kapalı ya da havuz boş: hiçbir kupon üretilmez.
        if (!config.IsActive || aktifHavuz.Count == 0)
        {
            return BadRequest(new { message = "Hediye kutusu şu anda kapalı. Daha sonra tekrar dene." });
        }

        // 1) Bekleme süresi — sunucuda zorlanır. İstemci ne kadar denerse denesin geçemez.
        var son = await SonHediyeKuponu(userId);
        if (son != null && son.CreatedAt.AddHours(config.CooldownHours) > now)
        {
            return Ok(new MysteryBoxResultDto
            {
                AlreadyPlayed = true,
                Won = PrizeToDto(EslesenOdul(havuz, son), son),
                NextAvailableAt = son.CreatedAt.AddHours(config.CooldownHours),
                Message = "Bugünkü hediye kutunu zaten açtın. Yeni kutu için yarın tekrar gel! 🎁"
            });
        }

        // 2) Kazanan ödül SUNUCUDA ve adminin verdiği ağırlıklara göre belirlenir; kalan
        //    kutular havuzdan rastgele (kazanan hariç) doldurulur — yani ekrandaki diğer
        //    kutular yalnızca "ne kaçırdın" gösterisidir, ihtimali etkilemez.
        var kazanan = AgirlikliSec(aktifHavuz);
        int kutuSayisi = KutuSayisi(config, aktifHavuz.Count);
        var kacirilanlar = aktifHavuz
            .Where(p => p.Id != kazanan.Id)
            .OrderBy(_ => Random.Shared.Next())
            .Take(kutuSayisi - 1)
            .ToList();

        // 3) Kazanılan ödülü gerçek, kişiye özel, tek kullanımlık kupona dönüştür.
        var coupon = new Coupon
        {
            Code = await BenzersizKodUret(),
            Type = kazanan.Type,
            Value = kazanan.Value,
            MinOrderTotal = kazanan.MinOrderTotal,
            MaxUses = 1,           // tek sipariş
            PerUserLimit = 1,      // ve o kişi için tek kez
            StartsAt = now,
            EndsAt = now.AddDays(config.CouponValidDays),
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

        return Ok(new MysteryBoxResultDto
        {
            AlreadyPlayed = false,
            Won = PrizeToDto(kazanan, coupon),
            Missed = kacirilanlar.Select(p => PrizeToDto(p, null)).ToList(),   // kodsuz — kullanılamaz
            NextAvailableAt = now.AddHours(config.CooldownHours),
            Message = $"Tebrikler! {kazanan.Emoji} {kazanan.Label} kazandın. Kupon kodun: {coupon.Code}"
        });
    }

    // ===================== ADMIN =====================

    // Havuzun tamamı + ayarlar + her ödülün gerçekleşen kazanılma sayısı.
    [HttpGet("admin")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetAdmin()
    {
        var config = await AyarGetirVeyaOlustur();
        var havuz = await HavuzGetirVeyaTohumla();

        // Kazanılma sayıları: hediye kuponları (Type, Value) ikilisiyle ödüle geri eşlenir —
        // ödülün kendisine FK tutmuyoruz, çünkü ödül silinse bile geçmiş kupon geçerli kalmalı.
        var hediyeKuponlari = await _context.Coupons
            .Where(c => c.Code.StartsWith(CodePrefix))
            .Select(c => new { c.Type, c.Value })
            .ToListAsync();

        int toplamAgirlik = havuz.Where(p => p.IsActive).Sum(p => Math.Max(0, p.Weight));

        var prizes = havuz.Select(p => new MysteryPrizeAdminDto
        {
            Id = p.Id,
            Emoji = p.Emoji,
            Label = p.Label,
            Type = p.Type,
            Value = p.Value,
            MinOrderTotal = p.MinOrderTotal,
            Weight = p.Weight,
            IsActive = p.IsActive,
            SortOrder = p.SortOrder,
            Chance = (p.IsActive && toplamAgirlik > 0)
                ? Math.Round(Math.Max(0, p.Weight) * 100.0 / toplamAgirlik, 1)
                : 0,
            WonCount = hediyeKuponlari.Count(c => c.Type == p.Type && c.Value == p.Value)
        }).ToList();

        return Ok(new MysteryBoxAdminDto
        {
            IsActive = config.IsActive,
            CooldownHours = config.CooldownHours,
            CouponValidDays = config.CouponValidDays,
            BoxCount = config.BoxCount,
            Prizes = prizes,
            TotalPlays = hediyeKuponlari.Count
        });
    }

    [HttpPut("admin/settings")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> UpdateSettings(MysteryBoxSettingsDto dto)
    {
        if (dto.CooldownHours < 1 || dto.CooldownHours > 720)
        {
            return BadRequest(new { message = "Bekleme süresi 1-720 saat arasında olmalı." });
        }
        if (dto.CouponValidDays < 1 || dto.CouponValidDays > 365)
        {
            return BadRequest(new { message = "Kupon geçerlilik süresi 1-365 gün arasında olmalı." });
        }
        if (dto.BoxCount < 1 || dto.BoxCount > 6)
        {
            return BadRequest(new { message = "Kutu sayısı 1-6 arasında olmalı." });
        }

        var config = await AyarGetirVeyaOlustur();
        config.IsActive = dto.IsActive;
        config.CooldownHours = dto.CooldownHours;
        config.CouponValidDays = dto.CouponValidDays;
        config.BoxCount = dto.BoxCount;

        Logla("Hediye kutusu ayarı güncellendi",
            $"Aktif: {dto.IsActive}, Bekleme: {dto.CooldownHours} saat, Kupon geçerliliği: {dto.CouponValidDays} gün, Kutu: {dto.BoxCount}.");

        await _context.SaveChangesAsync();
        return Ok(new { message = "Ayarlar kaydedildi." });
    }

    // Ödül ekle (Id boş) ya da güncelle (Id dolu).
    [HttpPost("admin/prizes")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> UpsertPrize(MysteryPrizeUpsertDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Label))
        {
            return BadRequest(new { message = "Ödül adı boş olamaz." });
        }
        if (dto.Type == CouponType.Percent && (dto.Value < 1 || dto.Value > 99))
        {
            return BadRequest(new { message = "Yüzde ödülün oranı 1-99 arasında olmalı." });
        }
        if (dto.Type == CouponType.Amount && dto.Value < 1)
        {
            return BadRequest(new { message = "Tutar ödülü en az 1 TL olmalı." });
        }
        if (dto.Weight < 0 || dto.Weight > 1000)
        {
            return BadRequest(new { message = "Ağırlık 0-1000 arasında olmalı." });
        }

        // Aynı (tip, değer) ikilisi havuzda iki kez bulunamaz: kazanılan kuponu ödüle geri
        // eşlerken ("bugün ne kazandın", kazanılma sayıları) bu ikili kimlik görevi görüyor.
        var havuz = await HavuzGetirVeyaTohumla();
        if (havuz.Any(p => p.Type == dto.Type && p.Value == dto.Value && p.Id != (dto.Id ?? 0)))
        {
            return BadRequest(new { message = "Aynı tip ve değerde bir ödül zaten var. Farklı bir değer gir." });
        }

        MysteryBoxPrize prize;
        bool yeni = dto.Id is null or 0;

        if (yeni)
        {
            prize = new MysteryBoxPrize
            {
                SortOrder = dto.SortOrder ?? (havuz.Count == 0 ? 1 : havuz.Max(p => p.SortOrder) + 1)
            };
            _context.MysteryBoxPrizes.Add(prize);
        }
        else
        {
            prize = havuz.FirstOrDefault(p => p.Id == dto.Id)
                    ?? await _context.MysteryBoxPrizes.FindAsync(dto.Id!.Value)
                    ?? throw new InvalidOperationException();
            if (dto.SortOrder != null) prize.SortOrder = dto.SortOrder.Value;
        }

        prize.Emoji = string.IsNullOrWhiteSpace(dto.Emoji) ? "🎁" : dto.Emoji.Trim();
        prize.Label = dto.Label.Trim();
        prize.Type = dto.Type;
        prize.Value = dto.Value;
        prize.MinOrderTotal = dto.MinOrderTotal;
        prize.Weight = dto.Weight;
        prize.IsActive = dto.IsActive;

        Logla(yeni ? "Hediye kutusuna ödül eklendi" : "Hediye kutusu ödülü güncellendi",
            $"“{prize.Label}” ({CouponService.Ozet(prize.Type, prize.Value)}), ağırlık {prize.Weight}, aktif: {prize.IsActive}.");

        await _context.SaveChangesAsync();
        return Ok(new { message = yeni ? "Ödül eklendi." : "Ödül güncellendi." });
    }

    // Ödülü havuzdan çıkar. Kazanılmış kuponlar etkilenmez (onlar bağımsız kayıtlardır).
    [HttpDelete("admin/prizes/{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> DeletePrize(int id)
    {
        var prize = await _context.MysteryBoxPrizes.FindAsync(id);
        if (prize == null)
        {
            return NotFound();
        }

        _context.MysteryBoxPrizes.Remove(prize);
        Logla("Hediye kutusu ödülü silindi", $"“{prize.Label}” havuzdan çıkarıldı.");
        await _context.SaveChangesAsync();
        return Ok(new { message = "Ödül silindi." });
    }

    // ===================== yardımcılar =====================

    // Bu kullanıcıya atanmış en yeni hediye-kutusu kuponu (yoksa null).
    private Task<Coupon?> SonHediyeKuponu(int userId) =>
        _context.Coupons
            .Where(c => c.Code.StartsWith(CodePrefix)
                        && c.AssignedUsers.Any(a => a.UserId == userId))
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync();

    // Kupondaki (Type, Value) ikilisini havuzdaki ödüle geri eşler; bulunamazsa
    // (ödül sonradan silinmiş/değişmişse) kupondan türetilmiş yedek bir ödül döner.
    private static MysteryBoxPrize EslesenOdul(List<MysteryBoxPrize> havuz, Coupon c) =>
        havuz.FirstOrDefault(p => p.Type == c.Type && p.Value == c.Value)
        ?? new MysteryBoxPrize
        {
            Emoji = "🎁",
            Label = CouponService.Ozet(c.Type, c.Value) + " İndirim",
            Type = c.Type,
            Value = c.Value,
            MinOrderTotal = c.MinOrderTotal
        };

    // Ödülü DTO'ya çevirir. coupon verilirse (kazanılan ödül) kod ve son kullanma dolar;
    // null ise (kaçırılan ödül) bu alanlar boş kalır — kullanılamaz.
    private static MysteryPrizeDto PrizeToDto(MysteryBoxPrize p, Coupon? coupon) => new()
    {
        Emoji = p.Emoji,
        Label = p.Label,
        Summary = CouponService.Ozet(p.Type, p.Value),
        MinOrderTotal = p.MinOrderTotal,
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

    private void Logla(string action, string details)
    {
        _context.Logs.Add(new Log
        {
            UserId = CurrentUserId,
            Username = User.Identity?.Name,
            Action = action,
            Details = details,
            Timestamp = DateTime.UtcNow
        });
    }
}
