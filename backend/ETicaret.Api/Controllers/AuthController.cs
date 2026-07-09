using ETicaret.Api.Models.Dtos;
using ETicaret.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace ETicaret.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly AuthService _authService;

    public AuthController(AuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterDto dto)
    {
        try
        {
            var username = await _authService.RegisterAsync(dto);
            return Ok(new { message = $"{username} başarıyla kaydedildi." });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginDto dto)
    {
        var result = await _authService.LoginAsync(dto);
        if (result == null)
        {
            return Unauthorized(new { message = "Kullanıcı adı veya şifre hatalı." });
        }

        return Ok(result);
    }
}
