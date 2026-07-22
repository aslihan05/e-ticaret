namespace ETicaret.Api.Models.Entities;

public class Review
{
    public int Id { get; set; }

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    // 1-5 yıldız. Sınır hem burada (veritabanı check'i yerine) hem controller'da doğrulanır.
    public int Rating { get; set; }

    // Yorum metni opsiyonel: yalnızca yıldız verip geçmek isteyen kullanıcıyı zorlamıyoruz.
    public string? Comment { get; set; }

    public DateTime CreatedAt { get; set; }

    // Kullanıcı yorumunu güncellerse dolar; "düzenlendi" işaretini basmak için gerekli.
    public DateTime? UpdatedAt { get; set; }
}

// Yorumun güvenilirliği "satın almış mı" kuralından gelir; bkz. ReviewsController.HasPurchased.
// Bu yüzden ayrıca bir "doğrulanmış alışveriş" bayrağı tutmuyoruz — yorum zaten ancak
// onaylanmış bir siparişi olan kullanıcı tarafından yazılabiliyor.
