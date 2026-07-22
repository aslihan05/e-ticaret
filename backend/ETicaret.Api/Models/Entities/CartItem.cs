namespace ETicaret.Api.Models.Entities;

public class CartItem {
    public int Id {get; set;}
    public int UserId { get; set;}  // Kim
    public User User {get; set;} = null!;
    public int ProductId {get; set;} // Neyi
    public Product Product {get; set;} = null!;

    public int Quantity { get; set;}  // Kaç adet

    // Rezervasyonun başladığı an. Sepete eklemek ürünü stoktan ayırtır, ama süresiz değil:
    // terk edilmiş bir sepet stoğu sonsuza kadar kilitlemesin diye rezervasyonun ömrü var
    // (bkz. StockService.ReservationWindow). Adet artırıldığında süre yeniden başlar.
    public DateTime AddedAt { get; set; }
}

// Sepetin bbir satırı