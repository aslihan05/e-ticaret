using ETicaret.Api.Data;
using ETicaret.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace ETicaret.Api.Services;

// Stok müsaitliğinin tek karar yeri.
//
// Product.Stock "depoda fiziksel olarak duran adet"tir ve yalnızca admin siparişi
// onayladığında düşer. Ama depoda duruyor olması "satılabilir" demek değildir —
// o adetlerin bir kısmı üzerinde başkalarının hakkı olabilir:
//
//   1. Sepetlerdeki adetler  -> "sepete ekle diyene kadar koruyalım": sepete eklemek ürünü ayırtır.
//      Süre sınırlıdır (ReservationWindow); yoksa terk edilmiş bir sepet stoğu sonsuza dek kilitler.
//   2. Onay bekleyen siparişlerdeki adetler -> müşteri siparişi verdi, admin henüz karar vermedi.
//      Stok düşmedi ama söz verildi; başkasına satılamaz. Bunun süresi yoktur (admin karar verene kadar).
//
// Onaylanan kalemler zaten Product.Stock'tan düşüldüğü için burada İKİNCİ KEZ sayılmaz.
//
//   Müsait = Product.Stock - (başkalarının aktif sepet rezervasyonları) - (onay bekleyen sipariş adetleri)
public class StockService
{
    private readonly AppDbContext _context;

    // Sepete eklenen ürünün ayırtıldığı süre. Bu süre dolduktan sonra ürün sepette kalmaya
    // devam eder ama artık stoğu kilitlemez — müşteri yine de stok müsaitse sipariş verebilir.
    public static readonly TimeSpan ReservationWindow = TimeSpan.FromMinutes(30);

    public StockService(AppDbContext context)
    {
        _context = context;
    }

    // Bir ürün için, verilen kullanıcı dışındaki herkesin stok üzerindeki hak iddiası.
    // exceptUserId: kendi sepetindeki adet, kendine karşı "dolu" sayılmamalı —
    // yoksa sepetinde 1 varken 2'ye çıkarmak isteyen kullanıcı kendi rezervasyonuna takılırdı.
    public async Task<int> ClaimedByOthersAsync(int productId, int? exceptUserId)
    {
        var now = DateTime.UtcNow;
        var gecerliRezervasyon = now - ReservationWindow;

        int sepetlerdeki = await _context.CartItems
            .Where(c => c.ProductId == productId
                && c.UserId != exceptUserId
                && c.AddedAt > gecerliRezervasyon)
            .SumAsync(c => (int?)c.Quantity) ?? 0;

        int bekleyenSiparislerdeki = await PendingOrderClaimsAsync(productId);

        return sepetlerdeki + bekleyenSiparislerdeki;
    }

    // Onay bekleyen siparişlerdeki adetler. Sipariş sahibi kim olursa olsun sayılır:
    // kullanıcının kendi bekleyen siparişi de stoğu tutar (iki kez sipariş edip ikisinin de
    // onaylanmasını bekleyemez).
    private async Task<int> PendingOrderClaimsAsync(int productId)
    {
        return await _context.OrderItems
            .Where(oi => oi.ProductId == productId
                && oi.Status == OrderStatus.Pending
                && oi.Order.Status == OrderStatus.Pending)
            .SumAsync(oi => (int?)oi.Quantity) ?? 0;
    }

    // Verilen kullanıcının bu üründen sepetine en fazla kaç adet koyabileceği.
    public async Task<int> AvailableForUserAsync(int productId, int userId)
    {
        var product = await _context.Products
            .AsNoTracking()
            .Where(p => p.Id == productId)
            .Select(p => new { p.Stock })
            .FirstOrDefaultAsync();

        if (product == null) return 0;

        int musait = product.Stock - await ClaimedByOthersAsync(productId, userId);
        return Math.Max(0, musait);   // Negatife düşmesin (veri tutarsızlığına karşı)
    }
}
