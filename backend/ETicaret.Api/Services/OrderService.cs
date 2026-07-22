using ETicaret.Api.Data;
using ETicaret.Api.Models.Dtos;
using ETicaret.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace ETicaret.Api.Services;

public class OrderService
{
    private readonly AppDbContext _context;
    private readonly StockService _stockService;
    private readonly EmailService _emailService;
    private readonly CouponService _couponService;

    // Müşteri siparişini yalnızca verildikten sonraki bu süre içinde iptal edebilir.
    // Tek yerde tanımlı: hem iptal kuralı hem "İptal Et" butonunun görünürlüğü buradan beslenir.
    public static readonly TimeSpan CancelWindow = TimeSpan.FromHours(1);

    // İptal edilebilir mi? Hem henüz onaylanmamış (Pending) olmalı, hem de süre dolmamış olmalı.
    public static bool CanCancel(Order order, DateTime nowUtc) =>
        order.Status == OrderStatus.Pending && nowUtc - order.CreatedAt <= CancelWindow;

    public OrderService(AppDbContext context, StockService stockService, EmailService emailService, CouponService couponService)
    {
        _context = context;
        _stockService = stockService;
        _emailService = emailService;
        _couponService = couponService;
    }

    public async Task<Order> CreateOrderFromCartAsync(int userId, CheckoutDto dto)
    {
        // Teslimat bilgileri olmadan sipariş oluşturulamaz
        if (string.IsNullOrWhiteSpace(dto.RecipientName) ||
            string.IsNullOrWhiteSpace(dto.Phone) ||
            string.IsNullOrWhiteSpace(dto.City) ||
            string.IsNullOrWhiteSpace(dto.District) ||
            string.IsNullOrWhiteSpace(dto.Address))
        {
            throw new Exception("Teslimat bilgileri (ad, telefon, il, ilçe, adres) zorunludur.");
        }

        var cartItems = await _context.CartItems
            .Include(c => c.Product)
            .Where(c => c.UserId == userId)
            .ToListAsync();

        if (cartItems.Count == 0)
        {
            throw new Exception("Sepetiniz boş.");
        }

        var order = new Order
        {
            UserId = userId,
            Status = OrderStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            RecipientName = dto.RecipientName.Trim(),
            Phone = dto.Phone.Trim(),
            City = dto.City.Trim(),
            District = dto.District.Trim(),
            Address = dto.Address.Trim()
        };

        // Sipariş oluşturulurken stok HİÇ doğrulanmıyordu: stokta 1 adet varken 5 adetlik
        // sipariş verilebiliyor, sorun ancak admin onaylamaya çalıştığında ortaya çıkıyordu.
        // Sepetteki kendi rezervasyonu kendine karşı sayılmadığı için (exceptUserId) burada
        // kontrol edilen şey: "başkalarının hakkı düşüldükten sonra bu adet hâlâ duruyor mu".
        foreach (var item in cartItems)
        {
            int musait = await _stockService.AvailableForUserAsync(item.ProductId, userId);
            if (item.Quantity > musait)
            {
                throw new Exception(musait == 0
                    ? $"{item.Product.Name}: ürün artık müsait değil, sepetinden çıkarman gerekiyor."
                    : $"{item.Product.Name}: stokta yalnızca {musait} adet kaldı, sepetindeki adedi azaltmalısın.");
            }
        }

        var now = DateTime.UtcNow;
        foreach (var item in cartItems)
        {
            order.OrderItems.Add(new OrderItem
            {
                ProductId = item.ProductId,
                // Ürün gezinimi bağlanır (sepetten zaten yüklü): controller sipariş sonrası
                // "tamamlandı" ekranı için ürün adı/görselini yeniden sorgulamadan okuyabilsin.
                Product = item.Product,
                Quantity = item.Quantity,
                // İndirim aktifse sipariş indirimli fiyattan kesilir
                UnitPrice = item.Product.EffectivePrice(now)
            });
        }

        // Kupon (varsa) sepetin SUNUCUDA hesaplanan toplamı üzerinden doğrulanır.
        // Tutar frontend'den alınsaydı, isteği elle düzenleyen biri "sepetim 10.000 TL"
        // deyip alt limitli kuponu hak etmeden geçirebilirdi.
        if (!string.IsNullOrWhiteSpace(dto.CouponCode))
        {
            decimal subtotal = order.OrderItems.Sum(i => i.UnitPrice * i.Quantity);
            var coupon = await _couponService.ValidateAsync(dto.CouponCode, userId, subtotal);
            CouponService.Uygula(order, coupon);
        }

        _context.Orders.Add(order);
        _context.CartItems.RemoveRange(cartItems);

        await ProfileAdresiniIlkKezDoldurAsync(userId, order);

        await _context.SaveChangesAsync();

        // "Siparişiniz alındı" maili. SaveChanges'ten SONRA çağrılır: sipariş garanti altında,
        // mail gönderilemese bile (kota, servis kesintisi) sipariş kaydı kaybolmaz.
        await _emailService.SendOrderStatusAsync(order.Id, OrderStatus.Pending);

        return order;
    }

    // Müşteri adresini bir kez girsin, sonraki siparişlerde sepet onu hazır getirsin diye
    // ilk siparişin teslimat bilgisi profile yazılır.
    //
    // Yalnızca profilde HİÇ adres yoksa yazılır. Her siparişte üzerine yazmak, "bu seferlik
    // arkadaşımın adresine gönder" diyen müşterinin kayıtlı adresini sessizce değiştirirdi.
    // Kayıtlı adresi güncellemek Hesabım sayfasının işi.
    private async Task ProfileAdresiniIlkKezDoldurAsync(int userId, Order order)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null) return;

        bool adresiVar = !string.IsNullOrWhiteSpace(user.Address)
            || !string.IsNullOrWhiteSpace(user.City)
            || !string.IsNullOrWhiteSpace(user.District);

        if (adresiVar) return;

        user.City = order.City;
        user.District = order.District;
        user.Address = order.Address;

        // Ad ve telefon ayrı düşünülür: adres boş olsa da bunlar dolu olabilir, ezilmemeli.
        if (string.IsNullOrWhiteSpace(user.FullName))
        {
            user.FullName = order.RecipientName;
        }

        if (string.IsNullOrWhiteSpace(user.Phone))
        {
            user.Phone = order.Phone;
        }
    }

    public async Task<List<Order>> GetOrderHistoryAsync(int userId)
    {
        return await _context.Orders
            .Include(o => o.OrderItems)
                .ThenInclude(oi => oi.Product)
            .Where(o => o.UserId == userId)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();
    }

    // Müşteri kendi siparişini yalnızca (1) henüz onaylanmadıysa ve (2) CancelWindow süresi
    // dolmadıysa iptal edebilir. Bekleyen siparişte stok henüz düşmediği için stok iadesi gerekmez.
    public async Task CancelOrderAsync(int orderId, int userId)
    {
        var order = await _context.Orders
            .FirstOrDefaultAsync(o => o.Id == orderId && o.UserId == userId);

        if (order == null)
        {
            throw new Exception("Sipariş bulunamadı.");
        }

        if (order.Status != OrderStatus.Pending)
        {
            throw new Exception("Bu sipariş artık iptal edilemez (yalnızca 'Bekliyor' durumundaki siparişler iptal edilebilir).");
        }

        // Süre kontrolü: butonu gizlemek yeterli değil — istek doğrudan API'ye de atılabilir,
        // o yüzden asıl kural burada, sunucuda uygulanır.
        if (!CanCancel(order, DateTime.UtcNow))
        {
            throw new Exception($"İptal süresi doldu. Siparişler yalnızca verildikten sonraki {CancelWindow.TotalHours:0.#} saat içinde iptal edilebilir.");
        }

        order.Status = OrderStatus.Cancelled;
        await _context.SaveChangesAsync();
    }

    // Kalem bazında karar: approvedItemIds içindekiler onaylanır, kalanlar reddedilir.
    // Hiç onaylanan yoksa sipariş Rejected, en az bir kalem onaylıysa Approved olur.
    public async Task DecideOrderAsync(int orderId, List<int> approvedItemIds, int adminUserId)
    {
        var order = await _context.Orders
            .Include(o => o.OrderItems)
                .ThenInclude(oi => oi.Product)
            .FirstOrDefaultAsync(o => o.Id == orderId);

        if (order == null)
        {
            throw new Exception("Sipariş bulunamadı.");
        }

        if (order.Status != OrderStatus.Pending)
        {
            throw new Exception("Bu sipariş zaten işleme alınmış.");
        }

        var approved = order.OrderItems.Where(i => approvedItemIds.Contains(i.Id)).ToList();

        // Önce onaylanacak kalemlerin stoğu yetiyor mu kontrol et, sonra düş
        foreach (var item in approved)
        {
            if (item.Product.Stock < item.Quantity)
            {
                throw new Exception($"Stok yetersiz: {item.Product.Name} (stokta {item.Product.Stock}, sipariş {item.Quantity}).");
            }
        }

        foreach (var item in order.OrderItems)
        {
            if (approved.Contains(item))
            {
                item.Status = OrderStatus.Approved;
                item.Product.Stock -= item.Quantity;
            }
            else
            {
                item.Status = OrderStatus.Rejected;
            }
        }

        order.Status = approved.Count > 0 ? OrderStatus.Approved : OrderStatus.Rejected;
        order.ApprovedAt = DateTime.UtcNow;
        order.ApprovedBy = adminUserId;

        await _context.SaveChangesAsync();

        // Onay/red kararı müşteriyi doğrudan ilgilendirir; mail kalemlerin son hâline göre gider
        // (kısmi onayda reddedilen kalemler maildeki toplama katılmaz).
        await _emailService.SendOrderStatusAsync(order.Id, order.Status);
    }

    public async Task ApproveOrderAsync(int orderId, int adminUserId)
    {
        var itemIds = await _context.OrderItems
            .Where(i => i.OrderId == orderId)
            .Select(i => i.Id)
            .ToListAsync();

        await DecideOrderAsync(orderId, itemIds, adminUserId);
    }

    public async Task RejectOrderAsync(int orderId, int adminUserId)
    {
        await DecideOrderAsync(orderId, new List<int>(), adminUserId);
    }

    // Sadece sonuçlanmış (Reddedildi/Teslim Edildi) siparişler silinebilir
    public async Task DeleteOrderAsync(int orderId)
    {
        var order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == orderId);

        if (order == null)
        {
            throw new Exception("Sipariş bulunamadı.");
        }

        if (order.Status != OrderStatus.Rejected &&
            order.Status != OrderStatus.Delivered &&
            order.Status != OrderStatus.Cancelled)
        {
            throw new Exception("Sadece reddedilmiş, teslim edilmiş veya iptal edilmiş siparişler silinebilir.");
        }

        _context.Orders.Remove(order);
        await _context.SaveChangesAsync();
    }

// Genel durum güncelleme: Onay/Red mevcut kuralları kullanır;
// Kargoda ve Teslim Edildi sadece doğru sıradan geçilebilir.
public async Task UpdateStatusAsync(int orderId, OrderStatus newStatus, int adminUserId)
{
    if (newStatus == OrderStatus.Approved)
    {
        await ApproveOrderAsync(orderId, adminUserId);
        return;
    }

    if (newStatus == OrderStatus.Rejected)
    {
        await RejectOrderAsync(orderId, adminUserId);
        return;
    }

    var order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == orderId);

    if (order == null)
    {
        throw new Exception("Sipariş bulunamadı.");
    }

    bool validTransition =
        (newStatus == OrderStatus.Shipped && order.Status == OrderStatus.Approved) ||
        (newStatus == OrderStatus.Delivered && order.Status == OrderStatus.Shipped);

    if (!validTransition)
    {
        throw new Exception("Geçersiz durum geçişi.");
    }

    order.Status = newStatus;
    await _context.SaveChangesAsync();

    // "Kargoya verildi" / "Teslim edildi" maili. Bu olayın frontend'i yoktur:
    // admin panelden tetikler, müşterinin tarayıcısı kapalıdır — mail buradan gitmek zorunda.
    await _emailService.SendOrderStatusAsync(order.Id, newStatus);
}

// searchTerm verilirse siparişler geniş bir alan kümesinde aranır: Sipariş No, Müşteri No,
// Müşteri Adı (kullanıcı adı / ad-soyad), E-posta, Alıcı adı, Telefon, İl, İlçe, Açık adres,
// siparişteki Ürün adı ve Tarih. Filtre EF Core üzerinden IQueryable olarak kurulur, yani
// arama VERİTABANINDA çalışır — tüm siparişleri RAM'e çekip C# tarafında elemeyiz.
public async Task<List<Order>> GetAllOrdersAsync(string? searchTerm = null)
{
    var query = _context.Orders
        .Include(o => o.User)
        .Include(o => o.OrderItems)
            .ThenInclude(oi => oi.Product)
        .AsQueryable();

    if (!string.IsNullOrWhiteSpace(searchTerm))
    {
        var term = searchTerm.Trim();

        // Tarih araması: kullanıcı "20.07.2026" / "2026-07-20" gibi bir tarih yazdıysa,
        // o güne (yerel gün) ait siparişler de eşleşsin. Ayraç içeren ve gerçekten tarihe
        // çözülen terimlerde devreye girer; "12" gibi salt sayılar sipariş/müşteri no olarak
        // aranmaya devam eder. Gün sınırı yerel saatten UTC'ye çevrilir (kayıtlar UTC tutulur).
        DateTime? dayStartUtc = null, dayEndUtc = null;
        bool ayracVar = term.Contains('.') || term.Contains('/') || term.Contains('-');
        if (ayracVar && DateTime.TryParse(term,
                new System.Globalization.CultureInfo("tr-TR"),
                System.Globalization.DateTimeStyles.None, out var parsed))
        {
            dayStartUtc = parsed.Date.ToUniversalTime();
            dayEndUtc = parsed.Date.AddDays(1).ToUniversalTime();
        }

        // E-posta yalnızca terim bir e-posta parçası gibiyse ("@" içeriyorsa) aranır.
        // Aksi halde "aslihan" gibi bir ad araması, e-postasında bu adı barındıran BAŞKA
        // bir müşteriyi (örn. adı Aslıhan olmayan ama e-postası aslihan...@... olan test
        // hesabı) sonuçlara karıştırıyordu. Ad/kullanıcı adı alanları her zaman aranır.
        bool epostaAra = term.Contains('@');

        // Id/UserId sayısaldır; ToString() SQL Server'da CONVERT'e çevrilir, böylece
        // "#12" yazmadan "12" ile sipariş/müşteri numarası aranabilir.
        query = query.Where(o =>
            o.Id.ToString().Contains(term) ||
            o.UserId.ToString().Contains(term) ||
            o.User.Username.Contains(term) ||
            (o.User.FullName != null && o.User.FullName.Contains(term)) ||
            (epostaAra && o.User.Email != null && o.User.Email.Contains(term)) ||
            (o.RecipientName != null && o.RecipientName.Contains(term)) ||
            (o.Phone != null && o.Phone.Contains(term)) ||
            (o.City != null && o.City.Contains(term)) ||
            (o.District != null && o.District.Contains(term)) ||
            (o.Address != null && o.Address.Contains(term)) ||
            o.OrderItems.Any(i => i.Product.Name.Contains(term)) ||
            (dayStartUtc != null && o.CreatedAt >= dayStartUtc && o.CreatedAt < dayEndUtc));
    }

    return await query
        .OrderByDescending(o => o.CreatedAt)
        .ToListAsync();
}
}