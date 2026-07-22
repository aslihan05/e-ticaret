using ETicaret.Api.Data;
using ETicaret.Api.Models.Dtos;
using ETicaret.Api.Models.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace ETicaret.Api.Services;

// Bloklu kullanıcı giriş denemesi: normal "hatalı şifre" (401) ile karıştırılmasın diye
// controller'ın 403 dönebilmesi için ayrı bir istisna türü.
public class BlockedUserException : Exception
{
    public BlockedUserException(string message) : base(message) { }
}

public class AuthService
{
    private readonly AppDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly PasswordHasher<User> _passwordHasher = new();

    public AuthService(AppDbContext context, IConfiguration configuration)
    {
        _context = context;
        _configuration = configuration;
    }

    // Kayıt kuralları tek yerde: hem kayıt formu hem admin'in kullanıcı ekleme formu buradan geçer.
    public const int MinPasswordLength = 6;
    public const int MinUsernameLength = 3;

    // Kullanıcı adı/şifre/e-posta kurallarını doğrular; sorun varsa Türkçe mesajla açıklar.
    // Boş şifreyle hesap açılabilmesi (ve o hesaba boş şifreyle girilebilmesi) gerçek bir açıktı.
    public static string? ValidateCredentials(string? username, string? password, string? email, bool passwordRequired = true)
    {
        if (string.IsNullOrWhiteSpace(username) || username.Trim().Length < MinUsernameLength)
        {
            return $"Kullanıcı adı en az {MinUsernameLength} karakter olmalı.";
        }

        if (passwordRequired || !string.IsNullOrEmpty(password))
        {
            if (string.IsNullOrWhiteSpace(password) || password.Length < MinPasswordLength)
            {
                return $"Şifre en az {MinPasswordLength} karakter olmalı ve boşluktan ibaret olamaz.";
            }
        }

        // Burada e-posta opsiyonel doğrulanır (admin panelinden şifre değiştirirken
        // e-posta gönderilmeyebilir); zorunluluk çağıran tarafın kararı.
        return ValidateEmail(email, required: false);
    }

    // E-posta kuralı tek yerde: kayıt formu, admin paneli ve "Hesabım" sayfası aynı kuraldan geçer.
    // required: alan boş bırakılabilir mi (kayıtta hayır, admin'in güncellemesinde evet).
    public static string? ValidateEmail(string? email, bool required)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return required ? "E-posta zorunlu." : null;
        }

        return IsValidEmail(email) ? null : "Geçerli bir e-posta adresi girin.";
    }

    // Basit biçim kontrolü: tek @ var mı, öncesi/sonrası dolu mu, alan adında nokta var mı.
    // (Tam RFC uyumlu doğrulama pratikte gereksiz karmaşık; asıl doğrulama e-posta göndermektir.)
    private static bool IsValidEmail(string email)
    {
        var parts = email.Trim().Split('@');
        if (parts.Length != 2) return false;

        var (local, domain) = (parts[0], parts[1]);
        return local.Length > 0
            && domain.Contains('.')
            && !domain.StartsWith('.')
            && !domain.EndsWith('.')
            && domain.Length >= 3;
    }

    public async Task<string> RegisterAsync(RegisterDto dto)
    {
        var hata = ValidateCredentials(dto.Username, dto.Password, dto.Email);
        if (hata != null)
        {
            throw new Exception(hata);
        }

        // Kayıtta e-posta zorunlu: sipariş bildirimlerinin tek kanalı bu.
        var epostaHatasi = ValidateEmail(dto.Email, required: true);
        if (epostaHatasi != null)
        {
            throw new Exception(epostaHatasi);
        }

        dto.Username = dto.Username.Trim();

        if (await _context.Users.AnyAsync(u => u.Username == dto.Username))
        {
            throw new Exception("Bu kullanıcı adı zaten kullanılıyor.");
        }

        // Aynı e-postayla birden fazla hesap açılması, "şifremi unuttum" gibi akışları
        // ileride imkânsız hale getirir — şimdiden engellenir.
        if (await _context.Users.AnyAsync(u => u.Email == dto.Email))
        {
            throw new Exception("Bu e-posta adresi zaten kayıtlı.");
        }

        var customerRole = await _context.Roles.FirstAsync(r => r.Name == "Customer");

        // Kullanıcılar listesinde en sona eklensin diye en büyük sıradan bir fazlası verilir
        int nextSortOrder = (await _context.Users.MaxAsync(u => (int?)u.SortOrder) ?? 0) + 1;

        var user = new User
        {
            Username = dto.Username,
            Email = dto.Email,
            Phone = string.IsNullOrWhiteSpace(dto.Phone) ? null : dto.Phone.Trim(),
            Address = string.IsNullOrWhiteSpace(dto.Address) ? null : dto.Address.Trim(),
            CreatedAt = DateTime.UtcNow,
            RoleId = customerRole.Id,
            SortOrder = nextSortOrder
        };

        user.PasswordHash = _passwordHasher.HashPassword(user, dto.Password);

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        _context.Logs.Add(new Log
        {
            UserId = user.Id,
            Action = "Kayıt oldu",
            Details = user.Username,
            Timestamp = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        return user.Username;
    }

    public async Task<AuthResponseDto?> LoginAsync(LoginDto dto)
{
    var user = await _context.Users
        .Include(u => u.Role)
        .FirstOrDefaultAsync(u => u.Username == dto.Username);

    if (user == null)
    {
        _context.Logs.Add(new Log
        {
            Action = "Başarısız giriş denemesi",
            Details = $"Kullanıcı adı bulunamadı: {dto.Username}",
            Timestamp = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();
        return null;
    }

    var result = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, dto.Password);
    if (result == PasswordVerificationResult.Failed)
    {
        _context.Logs.Add(new Log
        {
            UserId = user.Id,
            Action = "Başarısız giriş denemesi",
            Details = $"{user.Username}: yanlış şifre",
            Timestamp = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();
        return null;
    }

    if (user.IsBlocked)
    {
        _context.Logs.Add(new Log
        {
            UserId = user.Id,
            Action = "Engellenmiş kullanıcı giriş denemesi",
            Details = user.Username,
            Timestamp = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();
        throw new BlockedUserException("Hesabınız engellenmiş. Yönetici ile iletişime geçin.");
    }

    var token = GenerateJwtToken(user);

    _context.Logs.Add(new Log
    {
        UserId = user.Id,
        Action = "Giriş yaptı",
        Details = user.Username,
        Timestamp = DateTime.UtcNow
    });
    await _context.SaveChangesAsync();

    return new AuthResponseDto
    {
        Token = token,
        Username = user.Username,
        Role = user.Role.Name
    };
}

private string GenerateJwtToken(User user)
{
    var claims = new List<Claim>
    {
        new Claim(ClaimTypes.Name, user.Username),
        new Claim(ClaimTypes.Role, user.Role.Name),
        new Claim("UserId", user.Id.ToString())
    };

    var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]!));
    var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
    var expiryMinutes = double.Parse(_configuration["Jwt:ExpiryMinutes"]!);

    var token = new JwtSecurityToken(
        issuer: _configuration["Jwt:Issuer"],
        audience: _configuration["Jwt:Audience"],
        claims: claims,
        expires: DateTime.UtcNow.AddMinutes(expiryMinutes),
        signingCredentials: creds
    );

    return new JwtSecurityTokenHandler().WriteToken(token);
}

}