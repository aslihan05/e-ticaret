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
        var product = await _context.Products.FindAsync(dto.ProductId);
        if (product == null || !product.IsActive)
        {
            return BadRequest(new { message = "Ürün bulunamadı." });
        }

        var existing = await _context.CartItems
            .FirstOrDefaultAsync(c => c.UserId == CurrentUserId && c.ProductId == dto.ProductId);

        int newQuantity = (existing?.Quantity ?? 0) + dto.Quantity;

        // Sepetteki toplam adet stoğu aşamaz
        if (newQuantity > product.Stock)
        {
            return BadRequest(new { message = $"Stokta yalnızca {product.Stock} adet var." });
        }

        if (existing != null)
        {
            existing.Quantity = newQuantity;
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
        // remaining: bu üründen sepete daha kaç adet eklenebilir (0 -> "Tükendi")
        return Ok(new { remaining = product.Stock - newQuantity });
    }

    [HttpPut("{id}")]
public async Task<IActionResult> UpdateQuantity(int id, CartItemDto dto)
{
    var item = await _context.CartItems
        .Include(c => c.Product)
        .FirstOrDefaultAsync(c => c.Id == id && c.UserId == CurrentUserId);

    if (item == null)
    {
        return NotFound();
    }

    if (dto.Quantity > item.Product.Stock)
    {
        return BadRequest(new { message = $"Stokta yalnızca {item.Product.Stock} adet var." });
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