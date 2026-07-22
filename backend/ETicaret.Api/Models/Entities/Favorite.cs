namespace ETicaret.Api.Models.Entities;

// Kullanıcının "sonra bakarım" listesi. Sepetten farkı: adet yok, stok rezerve etmez,
// siparişe dönüşmez — sadece bir işaret.
public class Favorite
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    // Favori listesi en son eklenen üstte olacak şekilde sıralanır.
    public DateTime CreatedAt { get; set; }
}
