namespace ETicaret.Api.Models.Dtos;

// Sepeti siparişe çevirirken frontend'in gönderdiği teslimat bilgileri.
public class CheckoutDto
{
    public string RecipientName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;       // İl
    public string District { get; set; } = string.Empty;   // İlçe
    public string Address { get; set; } = string.Empty;

    // İsteğe bağlı kupon kodu. Boşsa indirimsiz sipariş oluşur.
    public string? CouponCode { get; set; }
}
