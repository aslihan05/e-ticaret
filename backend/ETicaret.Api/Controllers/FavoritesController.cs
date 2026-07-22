using ETicaret.Api.Data;
using ETicaret.Api.Models.Entities;
using ETicaret.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ETicaret.Api.Controllers;

// Favoriler ("sonra bakarım" listesi).
//
// Tüm uçlar token'daki kullanıcıya kilitlidir; hiçbir yerde dışarıdan userId alınmaz —
// alınsaydı istekteki id'yi değiştiren biri başkasının listesini okuyup değiştirebilirdi.
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class FavoritesController : ControllerBase
{
    private readonly AppDbContext _context;

    public FavoritesController(AppDbContext context)
    {
        _context = context;
    }

    private int CurrentUserId => int.Parse(User.FindFirst("UserId")!.Value);

    // Favori listesi sayfası: vitrin kartının gösterdiği her şey (fiyat, indirim, stok, puan).
    // Pasif (satıştan kaldırılmış) ürünler listelenmez — tıklanınca 404 veren kart olurdu.
    [HttpGet]
    public async Task<IActionResult> GetMine()
    {
        var now = DateTime.UtcNow;
        var rezervasyonSiniri = now - StockService.ReservationWindow;
        int userId = CurrentUserId;

        var favoriler = await _context.Favorites
            .Where(f => f.UserId == userId && f.Product.IsActive)
            .OrderByDescending(f => f.CreatedAt)   // En son eklenen üstte
            .Select(f => new
            {
                f.Product.Id,
                f.Product.Name,
                f.Product.Description,
                f.Product.Price,
                f.Product.ImageUrl,
                f.Product.CategoryId,
                CategoryName = f.Product.Category.Name,
                AddedAt = DateTime.SpecifyKind(f.CreatedAt, DateTimeKind.Utc),
                // Stok mantığı vitrindekiyle aynı: depodaki adetten başkalarının sepetlerinde
                // ayrılmış ve onay bekleyen siparişlere sözü verilmiş adetler düşülür.
                InStock = f.Product.Stock
                    - _context.CartItems
                        .Where(ci => ci.ProductId == f.ProductId && ci.AddedAt > rezervasyonSiniri)
                        .Sum(ci => ci.Quantity)
                    - _context.OrderItems
                        .Where(oi => oi.ProductId == f.ProductId
                            && oi.Status == OrderStatus.Pending
                            && oi.Order.Status == OrderStatus.Pending)
                        .Sum(oi => oi.Quantity)
                    > 0,
                HasDiscount = f.Product.DiscountPrice != null && f.Product.DiscountPrice < f.Product.Price
                    && (f.Product.DiscountStart == null || f.Product.DiscountStart <= now)
                    && (f.Product.DiscountEnd == null || f.Product.DiscountEnd >= now),
                DiscountedPrice = (f.Product.DiscountPrice != null && f.Product.DiscountPrice < f.Product.Price
                    && (f.Product.DiscountStart == null || f.Product.DiscountStart <= now)
                    && (f.Product.DiscountEnd == null || f.Product.DiscountEnd >= now)) ? f.Product.DiscountPrice : null,
                ReviewCount = f.Product.Reviews.Count(),
                AverageRating = f.Product.Reviews.Any() ? Math.Round(f.Product.Reviews.Average(r => r.Rating), 1) : 0
            })
            .ToListAsync();

        return Ok(favoriler);
    }

    // Yalnızca id listesi. Vitrin 36 ürünü basarken hangi kalplerin dolu olacağını
    // bilmek için ürün başına istek atmak yerine tek seferde bu listeyi çeker.
    [HttpGet("ids")]
    public async Task<IActionResult> GetMyIds()
    {
        int userId = CurrentUserId;
        var ids = await _context.Favorites
            .Where(f => f.UserId == userId)
            .Select(f => f.ProductId)
            .ToListAsync();

        return Ok(ids);
    }

    [HttpPost("{productId}")]
    public async Task<IActionResult> Add(int productId)
    {
        if (!await _context.Products.AnyAsync(p => p.Id == productId && p.IsActive))
        {
            return NotFound(new { message = "Ürün bulunamadı." });
        }

        int userId = CurrentUserId;

        // Zaten favorideyse hata değil: kullanıcı açısından istenen sonuç (ürün listede)
        // zaten sağlanmış durumda. Hata dönmek, iki sekmeden ekleyeni boş yere uyarırdı.
        if (await _context.Favorites.AnyAsync(f => f.UserId == userId && f.ProductId == productId))
        {
            return Ok(new { message = "Ürün zaten favorilerinde.", isFavorite = true });
        }

        _context.Favorites.Add(new Favorite
        {
            UserId = userId,
            ProductId = productId,
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        return Ok(new { message = "Favorilere eklendi.", isFavorite = true });
    }

    [HttpDelete("{productId}")]
    public async Task<IActionResult> Remove(int productId)
    {
        int userId = CurrentUserId;

        var favori = await _context.Favorites
            .FirstOrDefaultAsync(f => f.UserId == userId && f.ProductId == productId);

        // Ekleme gibi silme de sonucu garanti eder: kayıt yoksa da liste istenen halde.
        if (favori == null)
        {
            return Ok(new { message = "Ürün zaten favorilerinde değil.", isFavorite = false });
        }

        _context.Favorites.Remove(favori);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Favorilerden çıkarıldı.", isFavorite = false });
    }
}
