using ETicaret.Api.Data;
using ETicaret.Api.Models.Dtos;
using ETicaret.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ETicaret.Api.Controllers;

// "Hesabım" sayfası: kullanıcının kendi iletişim bilgilerini görüp güncellemesi.
//
// Bu uç noktaların var olma sebebi: sipariş bildirim mailleri User.Email'e gidiyor,
// ama e-postası olmayan bir müşterinin bunu düzeltmesinin hiçbir yolu yoktu —
// yalnızca admin panelinden girilebiliyordu.
//
// AuthController'dan ayrı bir controller: oradaki [EnableRateLimiting("AuthLimit")]
// (dakikada 8 istek) şifre denemelerine karşı konmuştu; profil okuma/güncellemeyi
// o sınıra sokmanın anlamı yok.
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ProfileController : ControllerBase
{
    private readonly AppDbContext _context;

    public ProfileController(AppDbContext context)
    {
        _context = context;
    }

    // Kimin profili olduğu HER ZAMAN imzalı token'dan okunur, istekten değil.
    // İstemcinin gönderdiği bir id'ye güvenmek, herkesin başkasının profilini
    // düzenleyebilmesi demek olurdu (IDOR).
    private int CurrentUserId => int.Parse(User.FindFirst("UserId")!.Value);

    [HttpGet]
    public async Task<IActionResult> GetProfile()
    {
        var profile = await _context.Users
            .AsNoTracking()
            .Where(u => u.Id == CurrentUserId)
            .Select(u => new ProfileDto
            {
                Username = u.Username,
                Email = u.Email,
                FullName = u.FullName,
                Phone = u.Phone,
                City = u.City,
                District = u.District,
                Address = u.Address,
                Role = u.Role.Name,
                CreatedAt = u.CreatedAt
            })
            .FirstOrDefaultAsync();

        if (profile == null)
        {
            return NotFound();
        }

        return Ok(profile);
    }

    [HttpPut]
    public async Task<IActionResult> UpdateProfile(ProfileUpdateDto dto)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == CurrentUserId);
        if (user == null)
        {
            return NotFound();
        }

        // E-posta zorunlu: kayıt formundaki kuralla aynı. Bildirimlerin tek kanalı bu olduğu
        // için kullanıcının kendi hesabını "e-postasız" bırakmasına izin verilmez.
        var hata = AuthService.ValidateEmail(dto.Email, required: true);
        if (hata != null)
        {
            return BadRequest(new { message = hata });
        }

        var yeniEposta = dto.Email!.Trim();

        // Başkasının e-postasını üstlenmek engellenir (kayıt formundaki kuralın aynısı).
        if (await _context.Users.AnyAsync(u => u.Email == yeniEposta && u.Id != CurrentUserId))
        {
            return BadRequest(new { message = "Bu e-posta adresi başka bir hesapta kayıtlı." });
        }

        bool epostaDegisti = !string.Equals(user.Email, yeniEposta, StringComparison.OrdinalIgnoreCase);

        static string? Temizle(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        user.Email = yeniEposta;
        user.FullName = Temizle(dto.FullName);
        user.Phone = Temizle(dto.Phone);
        user.City = Temizle(dto.City);
        user.District = Temizle(dto.District);
        user.Address = Temizle(dto.Address);

        // E-posta değişikliği güvenlik açısından önemli bir olay (bildirimlerin gittiği adres
        // değişiyor), o yüzden ayrıca loglanır.
        if (epostaDegisti)
        {
            _context.Logs.Add(new Models.Entities.Log
            {
                UserId = user.Id,
                Action = "E-posta adresi değiştirildi",
                Details = $"{user.Username}: yeni adres {yeniEposta}",
                Timestamp = DateTime.UtcNow
            });
        }

        await _context.SaveChangesAsync();
        return Ok(new { message = "Bilgilerin güncellendi." });
    }
}
