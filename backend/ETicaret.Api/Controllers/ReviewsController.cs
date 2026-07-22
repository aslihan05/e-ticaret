using ETicaret.Api.Data;
using ETicaret.Api.Models.Dtos;
using ETicaret.Api.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ETicaret.Api.Controllers;

// Ürün yorumları ve puanları.
//
// Yorumların değerini belirleyen tek şey güvenilirlikleri: herkesin yorum yazabildiği bir
// puan, puan değildir. Bu yüzden buradaki kural şu — yorum yazabilmek için ürünün
// ONAYLANMIŞ bir siparişte kullanıcıya teslim edilmiş olması gerekir (bkz. HasPurchasedAsync).
// Rotalar iki köke ayrılıyor: ürüne ait okuma/yazma "api/products/{id}/reviews" altında,
// mevcut bir yorumun düzenlenmesi/silinmesi "api/reviews/{id}" altında.
[ApiController]
public class ReviewsController : ControllerBase
{
    private readonly AppDbContext _context;

    public ReviewsController(AppDbContext context)
    {
        _context = context;
    }

    public const int MaxCommentLength = 1000;

    private int CurrentUserId => int.Parse(User.FindFirst("UserId")!.Value);

    // Yorum hakkı: kalemi onaylanmış VE siparişi iptal/red edilmemiş bir alım.
    // ProductsController.GetMyHistory'deki "gerçekleşen alım" tanımıyla birebir aynı.
    private Task<bool> HasPurchasedAsync(int userId, int productId) =>
        _context.OrderItems.AnyAsync(oi =>
            oi.ProductId == productId
            && oi.Order.UserId == userId
            && oi.Status == OrderStatus.Approved
            && oi.Order.Status != OrderStatus.Cancelled
            && oi.Order.Status != OrderStatus.Rejected);

    // Yorumlar herkese açık ama kullanıcı adları açık yazılmaz: "aslihan" -> "a****n".
    // Gerçek e-ticaret sitelerinin yaptığı gibi — yorum bir kimlik ifşası olmamalı.
    private static string MaskUsername(string username)
    {
        if (username.Length <= 2) return username[0] + "*";
        return $"{username[0]}{new string('*', username.Length - 2)}{username[^1]}";
    }

    // Ürünün tüm yorumları + puan özeti. Giriş şart değil; giriş varsa kullanıcının
    // kendi yorumu ve yorum yazma hakkı da (canReview) aynı cevapta döner —
    // frontend'in ikinci bir istek atmasına gerek kalmaz.
    [HttpGet("api/products/{productId}/reviews")]
    public async Task<IActionResult> GetForProduct(int productId)
    {
        if (!await _context.Products.AnyAsync(p => p.Id == productId))
        {
            return NotFound();
        }

        // Giriş yoksa userId null olur; "kendi yorumum" ve "yorum yazabilir miyim" sorularının
        // cevabı da doğal olarak yok/hayır olur.
        int? userId = User.Identity?.IsAuthenticated == true ? CurrentUserId : null;

        var reviews = await _context.Reviews
            .Where(r => r.ProductId == productId)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new
            {
                r.Id,
                Username = r.User.Username,
                r.Rating,
                r.Comment,
                CreatedAt = DateTime.SpecifyKind(r.CreatedAt, DateTimeKind.Utc),
                UpdatedAt = r.UpdatedAt,
                IsMine = userId != null && r.UserId == userId
            })
            .ToListAsync();

        // Maskeleme veritabanında değil bellekte yapılır (SQL'e çevrilemeyecek bir işlem).
        var items = reviews.Select(r => new
        {
            r.Id,
            Username = MaskUsername(r.Username),
            r.Rating,
            r.Comment,
            r.CreatedAt,
            r.UpdatedAt,
            r.IsMine
        });

        bool alreadyReviewed = reviews.Any(r => r.IsMine);

        return Ok(new
        {
            Average = reviews.Count > 0 ? Math.Round(reviews.Average(r => r.Rating), 1) : 0,
            Count = reviews.Count,
            // 1..5 yıldızın her birinden kaç adet var — yıldız dağılımı çubuklarını besler.
            Distribution = Enumerable.Range(1, 5).ToDictionary(
                star => star.ToString(),
                star => reviews.Count(r => r.Rating == star)),
            // Yorum kutusunu göstermeye değer mi: satın almış ve henüz yazmamışsa evet.
            CanReview = userId != null && !alreadyReviewed && await HasPurchasedAsync(userId.Value, productId),
            AlreadyReviewed = alreadyReviewed,
            Items = items
        });
    }

    [HttpPost("api/products/{productId}/reviews")]
    [Authorize]
    public async Task<IActionResult> Create(int productId, ReviewDto dto)
    {
        var hata = Validate(dto);
        if (hata != null)
        {
            return BadRequest(new { message = hata });
        }

        if (!await _context.Products.AnyAsync(p => p.Id == productId))
        {
            return NotFound();
        }

        int userId = CurrentUserId;

        if (!await HasPurchasedAsync(userId, productId))
        {
            return BadRequest(new { message = "Yorum yapabilmek için bu ürünü satın almış olmalısınız." });
        }

        if (await _context.Reviews.AnyAsync(r => r.ProductId == productId && r.UserId == userId))
        {
            return BadRequest(new { message = "Bu ürüne zaten yorum yaptınız. Mevcut yorumunuzu düzenleyebilirsiniz." });
        }

        var review = new Review
        {
            ProductId = productId,
            UserId = userId,
            Rating = dto.Rating,
            Comment = Temizle(dto.Comment),
            CreatedAt = DateTime.UtcNow
        };

        _context.Reviews.Add(review);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Yorumunuz yayınlandı.", review.Id });
    }

    [HttpPut("api/reviews/{id}")]
    [Authorize]
    public async Task<IActionResult> Update(int id, ReviewDto dto)
    {
        var hata = Validate(dto);
        if (hata != null)
        {
            return BadRequest(new { message = hata });
        }

        var review = await _context.Reviews.FindAsync(id);
        if (review == null)
        {
            return NotFound();
        }

        // Sadece yorumun sahibi düzenleyebilir. Admin de düzenleyemez: bir admin'in
        // müşterinin ağzından yorum değiştirebilmesi yorumların güvenilirliğini bitirir.
        // (Admin uygunsuz yorumu silebilir — bkz. Delete.)
        if (review.UserId != CurrentUserId)
        {
            return Forbid();
        }

        review.Rating = dto.Rating;
        review.Comment = Temizle(dto.Comment);
        review.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return Ok(new { message = "Yorumunuz güncellendi." });
    }

    [HttpDelete("api/reviews/{id}")]
    [Authorize]
    public async Task<IActionResult> Delete(int id)
    {
        var review = await _context.Reviews
            .Include(r => r.Product)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (review == null)
        {
            return NotFound();
        }

        bool isAdmin = User.IsInRole("Admin");
        if (review.UserId != CurrentUserId && !isAdmin)
        {
            return Forbid();
        }

        // Admin başkasının yorumunu sildiğinde iz kalmalı: moderasyon kararı sessizce olmaz.
        if (isAdmin && review.UserId != CurrentUserId)
        {
            _context.Logs.Add(new Log
            {
                UserId = CurrentUserId,
                Action = "Yorum silindi",
                Details = $"{review.Product.Name} (#{review.ProductId}) ürününe yazılan #{review.Id} numaralı yorum silindi.",
                Timestamp = DateTime.UtcNow
            });
        }

        _context.Reviews.Remove(review);
        await _context.SaveChangesAsync();
        return Ok(new { message = "Yorum silindi." });
    }

    private static string? Validate(ReviewDto dto)
    {
        if (dto.Rating < 1 || dto.Rating > 5)
        {
            return "Puan 1 ile 5 yıldız arasında olmalı.";
        }

        if (dto.Comment != null && dto.Comment.Trim().Length > MaxCommentLength)
        {
            return $"Yorum en fazla {MaxCommentLength} karakter olabilir.";
        }

        return null;
    }

    // Boşluktan ibaret yorum, yorum değildir; null'a çevrilir ki arayüz boş balon basmasın.
    private static string? Temizle(string? comment) =>
        string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
}
