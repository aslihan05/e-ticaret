namespace ETicaret.Api.Models.Dtos;

// "Hesabım" sayfasının gösterdiği bilgiler.
// Kullanıcı adı ve rol salt okunur döner — kullanıcı kendi rolünü değiştiremez,
// kullanıcı adı ise siparişlerin/logların bağlı olduğu kimlik olduğu için sabittir.
public class ProfileDto
{
    public string Username { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? FullName { get; set; }
    public string? Phone { get; set; }

    // Kayıtlı teslimat adresi: sepette her sipariş için yeniden sorulmaz, buradan doldurulur.
    public string? City { get; set; }
    public string? District { get; set; }
    public string? Address { get; set; }

    public string Role { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

// Kullanıcının kendi güncelleyebildiği alanlar — sadece bunlar.
// Rol, kullanıcı adı, şifre ve IsBlocked bilerek YOK: müşterinin kendi rolünü
// yükseltmesi ya da blokunu kaldırması mümkün olmamalı (yetki yükseltme açığı).
public class ProfileUpdateDto
{
    public string? Email { get; set; }
    public string? FullName { get; set; }
    public string? Phone { get; set; }
    public string? City { get; set; }
    public string? District { get; set; }
    public string? Address { get; set; }
}
