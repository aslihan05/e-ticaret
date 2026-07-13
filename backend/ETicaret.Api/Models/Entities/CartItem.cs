namespace ETicaret.Api.Models.Entities;

public class CartItem {
    public int Id {get; set;}
    public int UserId { get; set;}  // Kim
    public User User {get; set;} = null!;
    public int ProductId {get; set;} // Neyi
    public Product Product {get; set;} = null!;

    public int Quantity { get; set;}  // Kaç adet
}

// Sepetin bbir satırı