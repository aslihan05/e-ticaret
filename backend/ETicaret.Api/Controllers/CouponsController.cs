using ETicaret.Api.Data;
using ETicaret.Api.Models.Dtos;
using ETicaret.Api.Models.Entities;
using ETicaret.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ETicaret.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CouponsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly CouponService _couponService;

    public CouponsController(AppDbContext context, CouponService couponService)
    {
        _context = context;
        _couponService = couponService;
    }

    private int CurrentUserId => int.Parse(User.FindFirst("UserId")!.Value);

    // Sepetteki "Uygula" düğmesi. Sipariş oluşturmaz; yalnızca kuponun geçerli olup
    // olmadığını ve ne kadar indirim yapacağını söyler.
    //
    // Kuponu burada "rezerve etmiyoruz": müşteri kodu uygulayıp siparişi vermeyebilir.
    // Asıl bağlayıcı doğrulama checkout'ta, aynı servisle tekrar yapılır.
    [HttpPost("apply")]
    public async Task<IActionResult> Apply(ApplyCouponDto dto)
    {
        int userId = CurrentUserId;

        // Sepet tutarı müşterinin GERÇEK sepetinden hesaplanır; istekten gelen bir
        // tutara güvenilmez. Fiyat da ürünün o anki geçerli fiyatıdır.
        var now = DateTime.UtcNow;
        var cartItems = await _context.CartItems
            .Include(c => c.Product)
            .Where(c => c.UserId == userId)
            .ToListAsync();

        if (cartItems.Count == 0)
        {
            return BadRequest(new { message = "Sepetiniz boş." });
        }

        decimal subtotal = cartItems.Sum(c => c.Product.EffectivePrice(now) * c.Quantity);

        try
        {
            var coupon = await _couponService.ValidateAsync(dto.Code, userId, subtotal);

            // İndirimi, siparişin kullanacağı hesabın aynısıyla önizleriz: geçici bir
            // Order'a şartları kopyalayıp ona hesaplatmak, sepette gösterilen tutar ile
            // siparişte kesilecek tutarın aynı koddan çıkmasını garanti eder.
            var onizleme = new Order();
            CouponService.Uygula(onizleme, coupon);
            decimal indirim = onizleme.CalculateDiscount(subtotal);

            return Ok(new
            {
                Code = coupon.Code,
                Type = coupon.Type,
                Value = coupon.Value,
                Subtotal = subtotal,
                Discount = indirim,
                Total = subtotal - indirim,
                Message = $"{CouponService.Ozet(coupon.Type, coupon.Value)} indirim uygulandı."
            });
        }
        catch (CouponException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // "Kuponlarım" sayfası: müşterinin ŞU AN kullanabileceği kuponlar.
    //
    // Sepet tutarı burada bilinmediği için alt limit (MinOrderTotal) elemez, bilgi olarak
    // gösterilir: müşteri "100 TL üstü sepette geçerli" kuponu görüp sepetini ona göre
    // tamamlayabilmeli. Bağlayıcı denetim yine ValidateAsync'te.
    [HttpGet("my")]
    public async Task<IActionResult> GetMyCoupons()
    {
        int userId = CurrentUserId;
        var now = DateTime.UtcNow;

        var adaylar = await _context.Coupons
            // Atama listesi boşsa herkese açık; doluysa yalnızca listedekilere görünür.
            .Where(c => c.IsActive
                && (!c.AssignedUsers.Any() || c.AssignedUsers.Any(a => a.UserId == userId))
                && (c.StartsAt == null || c.StartsAt <= now)
                && (c.EndsAt == null || c.EndsAt >= now))
            .OrderByDescending(c => c.AssignedUsers.Any())   // bana özel olanlar üstte
            .ThenBy(c => c.EndsAt == null)                   // süresi dolmak üzere olanlar önce
            .ThenBy(c => c.EndsAt)
            .Select(c => new
            {
                c.Id,
                c.Code,
                c.Type,
                c.Value,
                c.MinOrderTotal,
                c.MaxUses,
                c.PerUserLimit,
                EndsAt = c.EndsAt,
                KisiyeOzel = c.AssignedUsers.Any(),
                // Sayım ALIŞVERİŞ (CheckoutId) bazında: sepetteki her ürün ayrı sipariş
                // doğurduğu için sipariş saymak, 3 ürün alan müşteriyi kuponu 3 kez
                // kullanmış gösterip kuponu listeden düşürürdü.
                ToplamKullanim = _context.Orders
                    .Where(o => o.CouponId == c.Id
                        && o.Status != OrderStatus.Cancelled
                        && o.Status != OrderStatus.Rejected)
                    .Select(o => o.CheckoutId).Distinct().Count(),
                KendiKullanimim = _context.Orders
                    .Where(o => o.CouponId == c.Id
                        && o.UserId == userId
                        && o.Status != OrderStatus.Cancelled
                        && o.Status != OrderStatus.Rejected)
                    .Select(o => o.CheckoutId).Distinct().Count()
            })
            .ToListAsync();

        // Limiti dolmuş kuponu listelemek anlamsız: müşteri deneyip "limit dolmuş"
        // hatası alırdı. Sayımlar yukarıda çekildiği için eleme burada yapılıyor.
        var sonuc = adaylar
            .Where(c => (c.MaxUses == null || c.ToplamKullanim < c.MaxUses)
                && (c.PerUserLimit == null || c.KendiKullanimim < c.PerUserLimit))
            .Select(c => new
            {
                c.Code,
                c.Type,
                c.Value,
                c.MinOrderTotal,
                c.EndsAt,
                c.KisiyeOzel,
                Ozet = CouponService.Ozet(c.Type, c.Value),
                KalanHakkim = c.PerUserLimit == null ? (int?)null : c.PerUserLimit - c.KendiKullanimim
            })
            .ToList();

        return Ok(sonuc);
    }

    /* ===== Admin: kupon yönetimi ===== */

    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetAll()
    {
        var coupons = await _context.Coupons
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new
            {
                c.Id,
                c.Code,
                c.Type,
                c.Value,
                UserIds = c.AssignedUsers.Select(a => a.UserId).ToList(),
                Usernames = c.AssignedUsers.Select(a => a.User.Username).ToList(),
                c.MinOrderTotal,
                c.MaxUses,
                c.PerUserLimit,
                StartsAt = c.StartsAt,
                EndsAt = c.EndsAt,
                c.IsActive,
                CreatedAt = DateTime.SpecifyKind(c.CreatedAt, DateTimeKind.Utc),
                // Kullanım sayısı kuponda tutulmuyor, siparişlerden sayılıyor. Sepetteki her
                // ürün ayrı sipariş doğurduğu için sayım ALIŞVERİŞ (CheckoutId) bazındadır —
                // limitleri uygulayan CouponService.KullanimSayisiAsync ile aynı ölçü.
                UsedCount = _context.Orders
                    .Where(o => o.CouponId == c.Id
                        && o.Status != OrderStatus.Cancelled
                        && o.Status != OrderStatus.Rejected)
                    .Select(o => o.CheckoutId)
                    .Distinct()
                    .Count()
            })
            .ToListAsync();

        return Ok(coupons);
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Create(CouponUpsertDto dto)
    {
        var hedefler = Hedefler(dto);
        var hata = Validate(dto) ?? await HedefKullanicilarGecerliMi(hedefler);
        if (hata != null) return BadRequest(new { message = hata });

        var kod = CouponService.Normalize(dto.Code);

        if (await _context.Coupons.AnyAsync(c => c.Code == kod))
        {
            return BadRequest(new { message = "Bu kupon kodu zaten var." });
        }

        var coupon = new Coupon { Code = kod, CreatedAt = DateTime.UtcNow };
        ApplyDto(coupon, dto);

        foreach (var uid in hedefler)
        {
            coupon.AssignedUsers.Add(new CouponUser { UserId = uid });
        }

        _context.Coupons.Add(coupon);
        await _context.SaveChangesAsync();

        Logla("Kupon oluşturuldu", $"{coupon.Code}: {Ozet(coupon)}");
        await _context.SaveChangesAsync();

        return Ok(new { message = "Kupon oluşturuldu.", coupon.Id });
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Update(int id, CouponUpsertDto dto)
    {
        var hedefler = Hedefler(dto);
        var hata = Validate(dto) ?? await HedefKullanicilarGecerliMi(hedefler);
        if (hata != null) return BadRequest(new { message = hata });

        var coupon = await _context.Coupons
            .Include(c => c.AssignedUsers)
            .FirstOrDefaultAsync(c => c.Id == id);
        if (coupon == null) return NotFound();

        var kod = CouponService.Normalize(dto.Code);
        if (kod != coupon.Code && await _context.Coupons.AnyAsync(c => c.Code == kod))
        {
            return BadRequest(new { message = "Bu kupon kodu zaten var." });
        }

        coupon.Code = kod;
        ApplyDto(coupon, dto);

        // Atama listesi tamamen yeniden kurulur: formdan gelen liste "olması gereken son hal"dir.
        // Çıkarılan müşterinin GEÇMİŞ siparişi etkilenmez (kupon şartları siparişe kopyalanmıştı),
        // yalnızca bundan sonra kullanamaz.
        coupon.AssignedUsers.Clear();
        foreach (var uid in hedefler)
        {
            coupon.AssignedUsers.Add(new CouponUser { CouponId = coupon.Id, UserId = uid });
        }

        await _context.SaveChangesAsync();

        // Not: burada yapılan değişiklik GEÇMİŞ siparişleri etkilemez — onlar kuponun
        // şartlarını sipariş anında kendi üzerlerine kopyalamıştı (bkz. Order.CouponValue).
        Logla("Kupon güncellendi", $"{coupon.Code}: {Ozet(coupon)}");
        await _context.SaveChangesAsync();

        return Ok(new { message = "Kupon güncellendi." });
    }

    // Kupon silinmez, kapatılır: silmek onu kullanmış siparişlerin bağını koparırdı.
    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Deactivate(int id)
    {
        var coupon = await _context.Coupons.FindAsync(id);
        if (coupon == null) return NotFound();

        coupon.IsActive = false;
        await _context.SaveChangesAsync();

        Logla("Kupon kapatıldı", coupon.Code);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Kupon kapatıldı." });
    }

    private static string? Validate(CouponUpsertDto dto)
    {
        var kod = CouponService.Normalize(dto.Code);

        if (kod.Length < 3)
        {
            return "Kupon kodu en az 3 karakter olmalı.";
        }

        // Kod adres/parametre içinde de geçebildiği için harf-rakam dışına izin verilmiyor
        if (!kod.All(char.IsLetterOrDigit))
        {
            return "Kupon kodu yalnızca harf ve rakamlardan oluşabilir.";
        }

        if (dto.Type == CouponType.Percent && (dto.Value < 1 || dto.Value > 99))
        {
            return "Yüzde indirim 1 ile 99 arasında olmalı.";
        }

        if (dto.Type == CouponType.Amount && dto.Value <= 0)
        {
            return "Tutar indirimi 0'dan büyük olmalı.";
        }

        if (dto.MinOrderTotal != null && dto.MinOrderTotal < 0)
        {
            return "Alt limit negatif olamaz.";
        }

        if (dto.MaxUses != null && dto.MaxUses < 1)
        {
            return "Kullanım limiti en az 1 olmalı.";
        }

        if (dto.PerUserLimit != null && dto.PerUserLimit < 1)
        {
            return "Kullanıcı başına limit en az 1 olmalı.";
        }

        if (dto.StartsAt != null && dto.EndsAt != null && dto.StartsAt >= dto.EndsAt)
        {
            return "Bitiş tarihi başlangıçtan sonra olmalı.";
        }

        return null;
    }

    // Formdan gelen hedef listesi: tekrarlar ayıklanır (aynı kişi iki kez seçilirse bileşik
    // anahtar ihlali olurdu), boş liste "herkese açık" demektir.
    private static List<int> Hedefler(CouponUpsertDto dto) =>
        dto.UserIds?.Distinct().ToList() ?? new List<int>();

    // Kupon kullanıcılara atanıyorsa hepsi gerçekten var olmalı; yoksa foreign key hatası
    // ham DbUpdateException olarak dönerdi.
    private async Task<string?> HedefKullanicilarGecerliMi(List<int> userIds)
    {
        if (userIds.Count == 0) return null;

        int bulunan = await _context.Users.CountAsync(u => userIds.Contains(u.Id));
        return bulunan == userIds.Count
            ? null
            : "Kuponun tanımlanacağı kullanıcılardan biri bulunamadı.";
    }

    private static void ApplyDto(Coupon coupon, CouponUpsertDto dto)
    {
        coupon.Type = dto.Type;
        coupon.Value = dto.Value;
        coupon.MinOrderTotal = dto.MinOrderTotal;
        coupon.MaxUses = dto.MaxUses;
        coupon.PerUserLimit = dto.PerUserLimit;
        coupon.StartsAt = dto.StartsAt;
        coupon.EndsAt = dto.EndsAt;
        coupon.IsActive = dto.IsActive;
    }

    private static string Ozet(Coupon c) =>
        CouponService.Ozet(c.Type, c.Value)
        + (c.MinOrderTotal != null ? $", min {CouponService.Bicimle(c.MinOrderTotal.Value)} TL" : "");

    private void Logla(string action, string details)
    {
        _context.Logs.Add(new Log
        {
            UserId = CurrentUserId,
            Action = action,
            Details = details,
            Timestamp = DateTime.UtcNow
        });
    }
}
