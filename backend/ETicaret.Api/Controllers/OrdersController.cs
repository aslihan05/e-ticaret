using ETicaret.Api.Models.Dtos;
using ETicaret.Api.Models.Entities;
using ETicaret.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ETicaret.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class OrdersController : ControllerBase
{
    private readonly OrderService _orderService;

    public OrdersController(OrderService orderService)
    {
        _orderService = orderService;
    }

    private int CurrentUserId => int.Parse(User.FindFirst("UserId")!.Value);

    // Siparişin ödenecek ara toplamı: reddedilen kalemler sayılmaz.
    // (Bekleyen sipariş için tüm kalemler Pending'dir, hepsi sayılır.)
    private static decimal AraToplam(Order order) =>
        order.OrderItems
            .Where(i => i.Status != OrderStatus.Rejected)
            .Sum(i => i.UnitPrice * i.Quantity);

    [HttpPost]
    public async Task<IActionResult> Checkout(CheckoutDto dto)
    {
        try
        {
            var order = await _orderService.CreateOrderFromCartAsync(CurrentUserId, dto);

            // Ham Order dönmek, içindeki Product entity'leriyle maliyet/stok/indirim bilgisini
            // sızdırıyordu. Frontend'in ihtiyacı olan tek şey siparişin oluştuğu bilgisi:
            decimal subtotal = order.OrderItems.Sum(i => i.UnitPrice * i.Quantity);
            decimal indirim = order.CalculateDiscount(subtotal);

            return Ok(new
            {
                order.Id,
                order.Status,
                CreatedAt = DateTime.SpecifyKind(order.CreatedAt, DateTimeKind.Utc),
                Subtotal = subtotal,
                order.CouponCode,
                Discount = indirim,
                Total = subtotal - indirim,
                // "Siparişiniz alındı" ekranında ne satın alındığını göstermek için kalemler:
                // ad + görsel + adet + birim/satır fiyatı. Maliyet/stok gibi iç alanlar sızmaz.
                Items = order.OrderItems.Select(i => new
                {
                    i.ProductId,
                    Name = i.Product.Name,
                    ImageUrl = i.Product.ImageUrl,
                    i.Quantity,
                    i.UnitPrice,
                    LineTotal = i.UnitPrice * i.Quantity
                })
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetHistory()
    {
        var orders = await _orderService.GetOrderHistoryAsync(CurrentUserId);

        // Ham Product entity'si dönmek ürünün maliyet (Cost) / stok / indirim gibi iç
        // bilgilerini müşteriye sızdırır. Sadece gerekli alanlarla sadeleştirilmiş projeksiyon:
        var now = DateTime.UtcNow;
        var result = orders.Select(o =>
        {
            decimal araToplam = AraToplam(o);
            decimal indirim = o.CalculateDiscount(araToplam);

            return new
            {
            o.Id,
            o.Status,
            o.CreatedAt,
            // "İptal Et" butonunu göstermek için: durum + süre kuralının sonucu
            CanCancel = OrderService.CanCancel(o, now),
            // Butonun yanında "kalan süre" yazabilmek için iptal hakkının bittiği an.
            // CreatedAt veritabanından Kind=Unspecified olarak döner; işaretlemezsek JSON'a
            // sonunda "Z" olmadan yazılır ve tarayıcı bunu YEREL saat sanıp süreyi yanlış hesaplar.
            CancelDeadline = DateTime.SpecifyKind(o.CreatedAt + OrderService.CancelWindow, DateTimeKind.Utc),
            o.RecipientName,
            o.Phone,
            o.City,
            o.District,
            o.Address,
            // Toplam ve indirim SUNUCUDA hesaplanır. Eskiden tarayıcı kalemleri toplayıp
            // kendi hesaplıyordu; kupon devreye girince aynı hesabın iki yerde yaşaması
            // (ve zamanla ayrışması) demek olurdu.
            //
            // Taban, reddedilen kalemler DIŞINDAKİ tutardır: kısmi onayda müşteri yalnızca
            // gerçekten aldığı kalemler için öder, indirim de o tutar üzerinden hesaplanır.
            o.CouponCode,
            Subtotal = araToplam,
            Discount = indirim,
            Total = araToplam - indirim,
            OrderItems = o.OrderItems.Select(i => new
            {
                i.Id,
                i.Status,
                i.Quantity,
                i.UnitPrice,
                // Id, sipariş kalemini ürün detayına bağlayabilmek için gönderilir.
                // Ürün id'si zaten /products listesinde herkese açık; sızdırdığı bir şey yok.
                Product = new { i.Product.Id, i.Product.Name, i.Product.ImageUrl }
            })
            };
        });

        return Ok(result);
    }

    [HttpPost("{id}/cancel")]
    public async Task<IActionResult> Cancel(int id)
    {
        try
        {
            await _orderService.CancelOrderAsync(id, CurrentUserId);
            return Ok(new { message = "Sipariş iptal edildi." });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}