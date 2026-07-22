using ETicaret.Api.Data;
using ETicaret.Api.Models.Dtos;
using ETicaret.Api.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ETicaret.Api.Controllers;

[ApiController]
[Route("api/[controller]")]


public class CategoriesController : ControllerBase
{
    private readonly AppDbContext _context;

    public CategoriesController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        // Önce belirlenen sıraya (SortOrder), eşitlikte ada göre listelenir
        var categories = await _context.Categories
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .ToListAsync();
        return Ok(categories);
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Create(CategoryDto dto)
    {
        var category = new Category { Name = dto.Name, ParentId = dto.ParentId, SortOrder = dto.SortOrder };
        _context.Categories.Add(category);
        await _context.SaveChangesAsync();
        return Ok(category);
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Update(int id, CategoryDto dto)
    {
        var category = await _context.Categories.FindAsync(id);
        if (category == null)
        {
            return NotFound();
        }

        category.Name = dto.Name;
        category.SortOrder = dto.SortOrder;

        // Üst kategori değişikliği: bir kategori sonradan alt kategori yapılabilir ya da
        // ana kategoriye çıkarılabilir (ParentId = null).
        if (dto.ParentId != category.ParentId)
        {
            if (dto.ParentId != null)
            {
                // Kendisinin altına taşınamaz — sonsuz döngü oluşur
                if (dto.ParentId == id)
                {
                    return BadRequest(new { message = "Bir kategori kendi alt kategorisi olamaz." });
                }

                var parent = await _context.Categories.FindAsync(dto.ParentId.Value);
                if (parent == null)
                {
                    return BadRequest(new { message = "Seçilen üst kategori bulunamadı." });
                }

                // Menü iki seviye: alt kategorinin altına kategori taşınamaz
                if (parent.ParentId != null)
                {
                    return BadRequest(new { message = "Bir alt kategorinin altına kategori taşınamaz (menü iki seviyeli)." });
                }

                // Kendi çocuğunun altına taşınırsa yine döngü olur (A > B iken A'yı B'nin altına almak)
                bool hasChildren = await _context.Categories.AnyAsync(c => c.ParentId == id);
                if (hasChildren)
                {
                    return BadRequest(new { message = "Alt kategorisi olan bir kategori başka bir kategorinin altına taşınamaz. Önce alt kategorilerini taşı." });
                }
            }

            category.ParentId = dto.ParentId;
        }

        await _context.SaveChangesAsync();
        return Ok(category);
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(int id)
    {
        var category = await _context.Categories.FindAsync(id);
        if (category == null)
        {
            return NotFound();
        }

        _context.Categories.Remove(category);
        await _context.SaveChangesAsync();
        return NoContent();
    }
}