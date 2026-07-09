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

    public async Task<string> RegisterAsync(RegisterDto dto)
    {
        if (await _context.Users.AnyAsync(u => u.Username == dto.Username))
        {
            throw new Exception("Bu kullanıcı adı zaten kullanılıyor.");
        }

        var customerRole = await _context.Roles.FirstAsync(r => r.Name == "Customer");

        var user = new User
        {
            Username = dto.Username,
            CreatedAt = DateTime.UtcNow,
            RoleId = customerRole.Id
        };

        user.PasswordHash = _passwordHasher.HashPassword(user, dto.Password);

        _context.Users.Add(user);
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
        return null;
    }

    var result = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, dto.Password);
    if (result == PasswordVerificationResult.Failed)
    {
        return null;
    }

    var token = GenerateJwtToken(user);

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