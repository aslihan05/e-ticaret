
using ETicaret.Api.Data;
using ETicaret.Api.Models.Dtos;
using ETicaret.Api.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ETicaret.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    private readonly AppDbContext _context;

    public ProductsController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? search, [FromQuery] bool includeInactive = false)
    {
        bool isAdmin = User.IsInRole("Admin");

        // Admin isterse pasif ürünleri de görür; müşteri her zaman sadece aktifleri görür
        var query = (isAdmin && includeInactive)
            ? _context.Products.AsQueryable()
            : _context.Products.Where(p => p.IsActive);

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(p => p.Name.Contains(search));
        }

        var now = DateTime.Now;

        // Stok SAYISI sadece admin'e gider; müşteri yalnızca var/yok (inStock) bilgisini görür
        var products = await query
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.Description,
                p.Price,
                p.ImageUrl,
                p.CategoryId,
                p.IsActive,
                Category = new { p.Category.Id, p.Category.Name },
                InStock = p.Stock > 0,
                Stock = isAdmin ? (int?)p.Stock : null,
                HasDiscount = p.DiscountPrice != null && p.DiscountPrice < p.Price
                    && (p.DiscountStart == null || p.DiscountStart <= now)
                    && (p.DiscountEnd == null || p.DiscountEnd >= now),
                DiscountedPrice = (p.DiscountPrice != null && p.DiscountPrice < p.Price
                    && (p.DiscountStart == null || p.DiscountStart <= now)
                    && (p.DiscountEnd == null || p.DiscountEnd >= now)) ? p.DiscountPrice : null,
                DiscountPrice = isAdmin ? p.DiscountPrice : null,
                DiscountStart = isAdmin ? p.DiscountStart : null,
                DiscountEnd = isAdmin ? p.DiscountEnd : null
            })
            .ToListAsync();

        return Ok(products);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        bool isAdmin = User.IsInRole("Admin");
        var now = DateTime.Now;

        var product = await _context.Products
            .Where(p => p.Id == id && (p.IsActive || isAdmin))
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.Description,
                p.Price,
                p.ImageUrl,
                p.CategoryId,
                p.IsActive,
                Category = new { p.Category.Id, p.Category.Name },
                InStock = p.Stock > 0,
                Stock = isAdmin ? (int?)p.Stock : null,
                HasDiscount = p.DiscountPrice != null && p.DiscountPrice < p.Price
                    && (p.DiscountStart == null || p.DiscountStart <= now)
                    && (p.DiscountEnd == null || p.DiscountEnd >= now),
                DiscountedPrice = (p.DiscountPrice != null && p.DiscountPrice < p.Price
                    && (p.DiscountStart == null || p.DiscountStart <= now)
                    && (p.DiscountEnd == null || p.DiscountEnd >= now)) ? p.DiscountPrice : null
            })
            .FirstOrDefaultAsync();

        if (product == null)
        {
            return NotFound();
        }

        return Ok(product);
    }

[HttpPost]
[Authorize(Roles = "Admin")]
public async Task<IActionResult> Create(ProductDto dto)
{
    // Aynı isimde pasif (silinmiş) ürün varsa yenisini açmak yerine onu canlandır.
    var inactive = await _context.Products
        .FirstOrDefaultAsync(p => !p.IsActive && p.Name == dto.Name);

    if (inactive != null)
    {
        inactive.IsActive = true;
        ApplyDto(inactive, dto);
        await _context.SaveChangesAsync();
        return Ok(inactive);
    }

    var product = new Product();
    ApplyDto(product, dto);

    _context.Products.Add(product);
    await _context.SaveChangesAsync();
    return Ok(product);
}


[HttpPut("{id}")]
[Authorize(Roles = "Admin")]
public async Task<IActionResult> Update(int id, ProductDto dto)
{
    var product = await _context.Products.FindAsync(id);
    if (product == null)
    {
        return NotFound();
    }

    ApplyDto(product, dto);

    await _context.SaveChangesAsync();
    return Ok(product);
}

private static void ApplyDto(Product product, ProductDto dto)
{
    product.Name = dto.Name;
    product.Description = dto.Description;
    product.Price = dto.Price;
    product.Stock = dto.Stock;
    product.ImageUrl = dto.ImageUrl;
    product.CategoryId = dto.CategoryId;
    product.DiscountPrice = dto.DiscountPrice;
    product.DiscountStart = dto.DiscountStart;
    product.DiscountEnd = dto.DiscountEnd;
}

// Sadece stok güncelle (Ürünler sekmesindeki hızlı stok kutusu için)
[HttpPut("{id}/stock")]
[Authorize(Roles = "Admin")]
public async Task<IActionResult> UpdateStock(int id, StockUpdateDto dto)
{
    var product = await _context.Products.FindAsync(id);
    if (product == null)
    {
        return NotFound();
    }

    if (dto.Stock < 0)
    {
        return BadRequest(new { message = "Stok negatif olamaz." });
    }

    product.Stock = dto.Stock;
    await _context.SaveChangesAsync();
    return Ok(new { message = "Stok güncellendi.", product.Id, product.Stock });
}

// İndirim planla / kaldır (tüm alanlar null gelirse indirim kalkar)
[HttpPut("{id}/discount")]
[Authorize(Roles = "Admin")]
public async Task<IActionResult> UpdateDiscount(int id, DiscountDto dto)
{
    var product = await _context.Products.FindAsync(id);
    if (product == null)
    {
        return NotFound();
    }

    if (dto.DiscountPrice != null && dto.DiscountPrice >= product.Price)
    {
        return BadRequest(new { message = "İndirimli fiyat normal fiyattan düşük olmalı." });
    }

    product.DiscountPrice = dto.DiscountPrice;
    product.DiscountStart = dto.DiscountStart;
    product.DiscountEnd = dto.DiscountEnd;

    await _context.SaveChangesAsync();
    return Ok(new { message = dto.DiscountPrice == null ? "İndirim kaldırıldı." : "İndirim kaydedildi." });
}

// Pasif ürünü yeniden satışa aç
[HttpPut("{id}/activate")]
[Authorize(Roles = "Admin")]
public async Task<IActionResult> Activate(int id)
{
    var product = await _context.Products.FindAsync(id);
    if (product == null)
    {
        return NotFound();
    }

    product.IsActive = true;
    await _context.SaveChangesAsync();
    return Ok(new { message = "Ürün yeniden aktif." });
}


// Hard delete yerine soft delete: geçmiş siparişlerin satırları korunur,
// ürün listeden kalkar, istenirse tekrar aktifleştirilebilir.
[HttpDelete("{id}")]
[Authorize(Roles = "Admin")]
public async Task<IActionResult> Delete(int id)
{
    var product = await _context.Products.FindAsync(id);
    if (product == null)
    {
        return NotFound();
    }

    product.IsActive = false;
    await _context.SaveChangesAsync();
    return NoContent();
}

}
