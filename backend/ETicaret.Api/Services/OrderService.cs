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

        var now = DateTime.Now;
        foreach (var item in cartItems)
        {
            order.OrderItems.Add(new OrderItem
            {
                ProductId = item.ProductId,
                Quantity = item.Quantity,
                // İndirim aktifse sipariş indirimli fiyattan kesilir
                UnitPrice = item.Product.EffectivePrice(now)
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

    // Kalem bazında karar: approvedItemIds içindekiler onaylanır, kalanlar reddedilir.
    // Hiç onaylanan yoksa sipariş Rejected, en az bir kalem onaylıysa Approved olur.
    public async Task DecideOrderAsync(int orderId, List<int> approvedItemIds, int adminUserId)
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

        var approved = order.OrderItems.Where(i => approvedItemIds.Contains(i.Id)).ToList();

        // Önce onaylanacak kalemlerin stoğu yetiyor mu kontrol et, sonra düş
        foreach (var item in approved)
        {
            if (item.Product.Stock < item.Quantity)
            {
                throw new Exception($"Stok yetersiz: {item.Product.Name} (stokta {item.Product.Stock}, sipariş {item.Quantity}).");
            }
        }

        foreach (var item in order.OrderItems)
        {
            if (approved.Contains(item))
            {
                item.Status = OrderStatus.Approved;
                item.Product.Stock -= item.Quantity;
            }
            else
            {
                item.Status = OrderStatus.Rejected;
            }
        }

        order.Status = approved.Count > 0 ? OrderStatus.Approved : OrderStatus.Rejected;
        order.ApprovedAt = DateTime.UtcNow;
        order.ApprovedBy = adminUserId;

        await _context.SaveChangesAsync();
    }

    public async Task ApproveOrderAsync(int orderId, int adminUserId)
    {
        var itemIds = await _context.OrderItems
            .Where(i => i.OrderId == orderId)
            .Select(i => i.Id)
            .ToListAsync();

        await DecideOrderAsync(orderId, itemIds, adminUserId);
    }

    public async Task RejectOrderAsync(int orderId, int adminUserId)
    {
        await DecideOrderAsync(orderId, new List<int>(), adminUserId);
    }

    // Sadece sonuçlanmış (Reddedildi/Teslim Edildi) siparişler silinebilir
    public async Task DeleteOrderAsync(int orderId)
    {
        var order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == orderId);

        if (order == null)
        {
            throw new Exception("Sipariş bulunamadı.");
        }

        if (order.Status != OrderStatus.Rejected && order.Status != OrderStatus.Delivered)
        {
            throw new Exception("Sadece reddedilmiş veya teslim edilmiş siparişler silinebilir.");
        }

        _context.Orders.Remove(order);
        await _context.SaveChangesAsync();
    }

// Genel durum güncelleme: Onay/Red mevcut kuralları kullanır;
// Kargoda ve Teslim Edildi sadece doğru sıradan geçilebilir.
public async Task UpdateStatusAsync(int orderId, OrderStatus newStatus, int adminUserId)
{
    if (newStatus == OrderStatus.Approved)
    {
        await ApproveOrderAsync(orderId, adminUserId);
        return;
    }

    if (newStatus == OrderStatus.Rejected)
    {
        await RejectOrderAsync(orderId, adminUserId);
        return;
    }

    var order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == orderId);

    if (order == null)
    {
        throw new Exception("Sipariş bulunamadı.");
    }

    bool validTransition =
        (newStatus == OrderStatus.Shipped && order.Status == OrderStatus.Approved) ||
        (newStatus == OrderStatus.Delivered && order.Status == OrderStatus.Shipped);

    if (!validTransition)
    {
        throw new Exception("Geçersiz durum geçişi.");
    }

    order.Status = newStatus;
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