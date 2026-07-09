using ETicaret.Api.Data;
using ETicaret.Api.Models.Dtos;
using ETicaret.Api.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ETicaret.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CartController : ControllerBase
{
    private readonly AppDbContext _context;

    public CartController(AppDbContext context)
    {
        _context = context;
    }

    private int CurrentUserId => int.Parse(User.FindFirst("UserId")!.Value);

    [HttpGet]
    public async Task<IActionResult> GetCart()
    {
        var items = await _context.CartItems
            .Include(c => c.Product)
            .Where(c => c.UserId == CurrentUserId)
            .ToListAsync();

        return Ok(items);
    }

    [HttpPost]
    public async Task<IActionResult> AddToCart(CartItemDto dto)
    {
        var existing = await _context.CartItems
            .FirstOrDefaultAsync(c => c.UserId == CurrentUserId && c.ProductId == dto.ProductId);

        if (existing != null)
        {
            existing.Quantity += dto.Quantity;
        }
        else
        {
            _context.CartItems.Add(new CartItem
            {
                UserId = CurrentUserId,
                ProductId = dto.ProductId,
                Quantity = dto.Quantity
            });
        }

        await _context.SaveChangesAsync();
        return Ok();
    }

    [HttpPut("{id}")]
public async Task<IActionResult> UpdateQuantity(int id, CartItemDto dto)
{
    var item = await _context.CartItems
        .FirstOrDefaultAsync(c => c.Id == id && c.UserId == CurrentUserId);

    if (item == null)
    {
        return NotFound();
    }

    item.Quantity = dto.Quantity;
    await _context.SaveChangesAsync();
    return Ok(item);
}

[HttpDelete("{id}")]
public async Task<IActionResult> RemoveFromCart(int id)
{
    var item = await _context.CartItems
        .FirstOrDefaultAsync(c => c.Id == id && c.UserId == CurrentUserId);

    if (item == null)
    {
        return NotFound();
    }

    _context.CartItems.Remove(item);
    await _context.SaveChangesAsync();
    return NoContent();
}
}