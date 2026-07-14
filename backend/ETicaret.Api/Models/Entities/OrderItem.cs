namespace ETicaret.Api.Models.Entities;

public class OrderItem
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;

    public int ProductId { get; set; }
    public Product Product {get; set;} = null!;

    public int Quantity {get; set; }
    public decimal UnitPrice {get; set;}   // Sipariş anındaki fiyatın anlık görüntüsüdür.

    // Kalem bazında karar: admin siparişin bir kısmını onaylayıp bir kısmını reddedebilir.
    // Aynı enum kullanılır; kalemler için sadece Pending/Approved/Rejected anlamlıdır.
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
}