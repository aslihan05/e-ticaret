using ETicaret.Api.Data;
using ETicaret.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ETicaret.Api.Models.Dtos;
using Microsoft.EntityFrameworkCore;
using ETicaret.Api.Models.Entities;


namespace ETicaret.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class AdminController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly OrderService _orderService;

    public AdminController(AppDbContext context, OrderService orderService)
    {
        _context = context;
        _orderService = orderService;
    }

    private int CurrentUserId => int.Parse(User.FindFirst("UserId")!.Value);

    [HttpGet("orders")]
    public async Task<IActionResult> GetAllOrders()
    {
        var orders = await _orderService.GetAllOrdersAsync();
        return Ok(orders);
    }

    [HttpPut("orders/{id}/approve")]
    public async Task<IActionResult> ApproveOrder(int id)
    {
        try
        {
            await _orderService.ApproveOrderAsync(id, CurrentUserId);
            return Ok(new { message = "Sipariş onaylandı." });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("orders/{id}/reject")]
    public async Task<IActionResult> RejectOrder(int id)
    {
        try
        {
            await _orderService.RejectOrderAsync(id, CurrentUserId);
            return Ok(new { message = "Sipariş reddedildi." });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
    [HttpGet("users")]
public async Task<IActionResult> GetAllUsers()
{
    var users = await _context.Users
        .Include(u => u.Role)
        .Select(u => new UserSummaryDto
        {
            Id = u.Id,
            Username = u.Username,
            Role = u.Role.Name,
            CreatedAt = u.CreatedAt
        })
        .ToListAsync();

    return Ok(users);
}

[HttpDelete("users/{id}")]
public async Task<IActionResult> DeleteUser(int id)
{
    var user = await _context.Users.FindAsync(id);
    if (user == null)
    {
        return NotFound();
    }

    try
    {
        _context.Users.Remove(user);
        await _context.SaveChangesAsync();
        return NoContent();
    }
    catch (DbUpdateException)
    {
        return BadRequest(new { message = "Bu kullanıcının siparişleri olduğu için silinemiyor." });
    }
}

[HttpGet("logs")]
public async Task<IActionResult> GetLogs()
{
    var logs = await _context.Logs
        .OrderByDescending(l => l.Timestamp)
        .Take(100)
        .ToListAsync();

    return Ok(logs);
}

[HttpGet("analytics")]
public async Task<IActionResult> GetAnalytics()
{
    var approvedOrders = await _context.Orders
        .Where(o => o.Status == OrderStatus.Approved)
        .Include(o => o.OrderItems)
        .ToListAsync();

    var totalRevenue = approvedOrders
        .SelectMany(o => o.OrderItems)
        .Sum(oi => oi.UnitPrice * oi.Quantity);

    var bestSellingProduct = await _context.OrderItems
        .Where(oi => oi.Order.Status == OrderStatus.Approved)
        .GroupBy(oi => oi.Product.Name)
        .Select(g => new { ProductName = g.Key, TotalSold = g.Sum(oi => oi.Quantity) })
        .OrderByDescending(g => g.TotalSold)
        .FirstOrDefaultAsync();

    var analytics = new
    {
        TotalOrders = await _context.Orders.CountAsync(),
        ApprovedOrders = approvedOrders.Count,
        PendingOrders = await _context.Orders.CountAsync(o => o.Status == OrderStatus.Pending),
        RejectedOrders = await _context.Orders.CountAsync(o => o.Status == OrderStatus.Rejected),
        TotalRevenue = totalRevenue,
        BestSellingProduct = bestSellingProduct?.ProductName,
        BestSellingProductQuantity = bestSellingProduct?.TotalSold
    };

    return Ok(analytics);
}

}
