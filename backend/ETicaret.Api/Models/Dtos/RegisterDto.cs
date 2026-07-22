namespace ETicaret.Api.Models.Dtos;

public class RegisterDto {
    public string Username {get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    // Kayıtta zorunlu değil; kullanıcı boş bırakabilir, sonra profilinden doldurabilir.
    public string? Phone { get; set; }
    public string? Address { get; set; }

    public string Password { get; set; } = string.Empty;
}