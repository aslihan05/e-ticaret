using ETicaret.Api.Data;
using ETicaret.Api.Models.Dtos;
using ETicaret.Api.Models.Entities;
using ETicaret.Api.Services;
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
    private readonly StockService _stockService;

    public CartController(AppDbContext context, StockService stockService)
    {
        _context = context;
        _stockService = stockService;
    }

    private int CurrentUserId => int.Parse(User.FindFirst("UserId")!.Value);

    // Ham CartItem dönmek, içindeki Product entity'siyle birlikte ürünün MALİYETİNİ (Cost),
    // stok sayısını ve indirim planını müşteriye sızdırıyordu. Products/Orders uçlarındaki
    // projeksiyon deseninin aynısı burada da uygulanır: sadece sepetin ihtiyacı olan alanlar.
    [HttpGet]
    public async Task<IActionResult> GetCart()
    {
        var items = await _context.CartItems
            .Include(c => c.Product)
            .Where(c => c.UserId == CurrentUserId)
            .ToListAsync();

        var now = DateTime.UtcNow;
        var result = new List<object>();

        foreach (var item in items)
        {
            // Fiyat kararı sunucuda verilir: frontend'in indirim tarih mantığını
            // tekrar hesaplaması (ve sunucudan farklı sonuca varması) riskini ortadan kaldırır.
            decimal unitPrice = item.Product.EffectivePrice(now);
            bool indirimli = item.Product.IsDiscountActive(now);

            // "+" butonunun açık kalıp kalmayacağı: stok sayısını sızdırmadan,
            // yalnızca "bir tane daha ekleyebilir misin" cevabı gönderilir.
            int musait = await _stockService.AvailableForUserAsync(item.ProductId, CurrentUserId);

            result.Add(new
            {
                item.Id,
                item.ProductId,
                item.Quantity,
                UnitPrice = unitPrice,
                LineTotal = unitPrice * item.Quantity,
                CanIncrease = item.Quantity < musait,
                Product = new
                {
                    item.Product.Id,
                    item.Product.Name,
                    item.Product.ImageUrl,
                    item.Product.Price,
                    HasDiscount = indirimli
                }
            });
        }

        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> AddToCart(CartItemDto dto)
    {
        if (dto.Quantity < 1)
        {
            return BadRequest(new { message = "Adet en az 1 olmalı." });
        }

        var product = await _context.Products.FindAsync(dto.ProductId);
        if (product == null || !product.IsActive)
        {
            return BadRequest(new { message = "Ürün bulunamadı." });
        }

        var existing = await _context.CartItems
            .FirstOrDefaultAsync(c => c.UserId == CurrentUserId && c.ProductId == dto.ProductId);

        int newQuantity = (existing?.Quantity ?? 0) + dto.Quantity;

        // Stok kontrolü artık ham Stock'a değil, MÜSAİT adede bakar: başkalarının
        // sepetlerinde ayrılmış ve onay bekleyen siparişlere sözü verilmiş adetler düşülür.
        int musait = await _stockService.AvailableForUserAsync(dto.ProductId, CurrentUserId);

        if (newQuantity > musait)
        {
            return BadRequest(new
            {
                message = musait == 0
                    ? "Bu ürün şu anda müsait değil (stoktaki adetler başka sepetlerde ayrılmış olabilir)."
                    : $"Bu üründen en fazla {musait} adet alabilirsin."
            });
        }

        if (existing != null)
        {
            existing.Quantity = newQuantity;
            existing.AddedAt = DateTime.UtcNow;   // Adet artınca rezervasyon süresi yenilenir
        }
        else
        {
            _context.CartItems.Add(new CartItem
            {
                UserId = CurrentUserId,
                ProductId = dto.ProductId,
                Quantity = dto.Quantity,
                AddedAt = DateTime.UtcNow
            });
        }

        await _context.SaveChangesAsync();
        // remaining: bu üründen sepete daha kaç adet eklenebilir (0 -> "Tükendi")
        return Ok(new { remaining = musait - newQuantity });
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

        if (dto.Quantity < 1)
        {
            return BadRequest(new { message = "Adet en az 1 olmalı." });
        }

        int musait = await _stockService.AvailableForUserAsync(item.ProductId, CurrentUserId);

        if (dto.Quantity > musait)
        {
            return BadRequest(new { message = $"Bu üründen en fazla {musait} adet alabilirsin." });
        }

        item.Quantity = dto.Quantity;
        item.AddedAt = DateTime.UtcNow;   // Rezervasyon süresi yenilenir
        await _context.SaveChangesAsync();

        return Ok(new { message = "Adet güncellendi." });
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

        // Sepetten çıkarmak rezervasyonu da serbest bırakır (satır silindiği için)
        _context.CartItems.Remove(item);
        await _context.SaveChangesAsync();
        return NoContent();
    }
}
