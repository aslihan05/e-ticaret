using ETicaret.Api.Data;
using ETicaret.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace ETicaret.Api.Services;

public class OrderService
{
    private readonly AppDbContext _context;

    public OrderService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<Order> CreateOrderFromCartAsync(int userId)
    {
        var cartItems = await _context.CartItems
            .Include(c => c.Product)
            .Where(c => c.UserId == userId)
            .ToListAsync();

        if (cartItems.Count == 0)
        {
            throw new Exception("Sepetiniz boş.");
        }

        var order = new Order
        {
            UserId = userId,
            Status = OrderStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        foreach (var item in cartItems)
        {
            order.OrderItems.Add(new OrderItem
            {
                ProductId = item.ProductId,
                Quantity = item.Quantity,
                UnitPrice = item.Product.Price
            });
        }

        _context.Orders.Add(order);
        _context.CartItems.RemoveRange(cartItems);

        await _context.SaveChangesAsync();

        return order;
    }

    public async Task<List<Order>> GetOrderHistoryAsync(int userId)
    {
        return await _context.Orders
            .Include(o => o.OrderItems)
                .ThenInclude(oi => oi.Product)
            .Where(o => o.UserId == userId)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();
    }

    public async Task ApproveOrderAsync(int orderId, int adminUserId)
{
    var order = await _context.Orders
        .Include(o => o.OrderItems)
            .ThenInclude(oi => oi.Product)
        .FirstOrDefaultAsync(o => o.Id == orderId);

    if (order == null)
    {
        throw new Exception("Sipariş bulunamadı.");
    }

    if (order.Status != OrderStatus.Pending)
    {
        throw new Exception("Bu sipariş zaten işleme alınmış.");
    }

    foreach (var item in order.OrderItems)
    {
        item.Product.Stock -= item.Quantity;
    }

    order.Status = OrderStatus.Approved;
    order.ApprovedAt = DateTime.UtcNow;
    order.ApprovedBy = adminUserId;

    await _context.SaveChangesAsync();
}

public async Task RejectOrderAsync(int orderId, int adminUserId)
{
    var order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == orderId);

    if (order == null)
    {
        throw new Exception("Sipariş bulunamadı.");
    }

    if (order.Status != OrderStatus.Pending)
    {
        throw new Exception("Bu sipariş zaten işleme alınmış.");
    }

    order.Status = OrderStatus.Rejected;
    order.ApprovedAt = DateTime.UtcNow;
    order.ApprovedBy = adminUserId;

    await _context.SaveChangesAsync();
}

public async Task<List<Order>> GetAllOrdersAsync()
{
    return await _context.Orders
        .Include(o => o.User)
        .Include(o => o.OrderItems)
            .ThenInclude(oi => oi.Product)
        .OrderByDescending(o => o.CreatedAt)
        .ToListAsync();
}
}