using System.Collections.Generic;                 namespace ETicaret.Api.Models.Entities;

public enum OrderStatus
{
    Pending,
    Approved,
    Rejected
}

public class Order
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User {get; set;} = null!;

    public OrderStatus Status {get; set;} = OrderStatus.Pending;

    public DateTime CreatedAt {get; set; }
    public DateTime? ApprovedAt {get; set;}

    public int? ApprovedBy {get; set;}
    public User? ApprovedByUser {get; set;}

    public ICollection<OrderItem> OrderItems { get; set; } = new 
    List<OrderItem>();
}