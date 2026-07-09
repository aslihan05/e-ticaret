
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
    public async Task<IActionResult> GetAll([FromQuery] string? search)
    {
        var query = _context.Products.Where(p => p.IsActive);

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(p => p.Name.Contains(search));
        }

        var products = await query.Include(p => p.Category).ToListAsync();
        return Ok(products);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var product = await _context.Products
            .Include(p => p.Category)
            .FirstOrDefaultAsync(p => p.Id == id);

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
    var product = new Product
    {
        Name = dto.Name,
        Description = dto.Description,
        Price = dto.Price,
        Stock = dto.Stock,
        ImageUrl = dto.ImageUrl,
        CategoryId = dto.CategoryId
    };

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

    product.Name = dto.Name;
    product.Description = dto.Description;
    product.Price = dto.Price;
    product.Stock = dto.Stock;
    product.ImageUrl = dto.ImageUrl;
    product.CategoryId = dto.CategoryId;

    await _context.SaveChangesAsync();
    return Ok(product);
}


[HttpDelete("{id}")]
[Authorize(Roles = "Admin")]
public async Task<IActionResult> Delete(int id)
{
    var product = await _context.Products.FindAsync(id);
    if (product == null)
    {
        return NotFound();
    }

    _context.Products.Remove(product);
    await _context.SaveChangesAsync();
    return NoContent();
}

}