using System.Collections.Generic;                 namespace ETicaret.Api.Models.Entities;

public enum OrderStatus    // enum geçerli değerleri derleyiciye denetletir
{
    Pending,  // OrderStatus.Pending ile her sipariş taslaktaki kurala uygun olarak "onay bekliyor" doğar.
    Approved,
    Rejected
}

public class Order
{
    public int Id { get; set; }
    public int UserId { get; set; }  // Sipariş veren
    public User User {get; set;} = null!;

    public OrderStatus Status {get; set;} = OrderStatus.Pending;

    public DateTime CreatedAt {get; set; }
    public DateTime? ApprovedAt {get; set;}

    public int? ApprovedBy {get; set;}     // int? -> Çünkü henüz onaylanmamış siparişin onaylayanı yoktur.
    public User? ApprovedByUser {get; set;}  // Onaylayan admin

    public ICollection<OrderItem> OrderItems { get; set; } = new   // Listenin birçok sipariş satırı olur.
    List<OrderItem>();  // Boşaltıyorurz ki checkout'ta order.OrderItems.Add(...) derken null hatası almayalım.
}