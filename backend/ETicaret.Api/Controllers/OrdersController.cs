using ETicaret.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ETicaret.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class OrdersController : ControllerBase
{
    private readonly OrderService _orderService;

    public OrdersController(OrderService orderService)
    {
        _orderService = orderService;
    }

    private int CurrentUserId => int.Parse(User.FindFirst("UserId")!.Value);

    [HttpPost]
    public async Task<IActionResult> Checkout()
    {
        try
        {
            var order = await _orderService.CreateOrderFromCartAsync(CurrentUserId);
            return Ok(order);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetHistory()
    {
        var orders = await _orderService.GetOrderHistoryAsync(CurrentUserId);
        return Ok(orders);
    }
}