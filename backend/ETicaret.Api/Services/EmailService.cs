using System.Text;
using System.Text.Json;
using ETicaret.Api.Data;
using ETicaret.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace ETicaret.Api.Services;

// Sipariş durumu değiştiğinde müşteriye bilgilendirme maili atar.
//
// NEDEN FRONTEND'DEN DEĞİL DE BURADAN?
//  1. "Kargoya verildi" olayı admin panelden tetiklenir; müşterinin tarayıcısı o an kapalıdır.
//     Gönderilecek yerde çalışan bir frontend yok.
//  2. Frontend'den gönderilebilseydi, konsolu açan herkes bizim kotamızdan istediği adrese
//     "siparişiniz kargoda" maili atabilirdi. Alıcı adresi burada order.User.Email'den okunur,
//     yani istemci tarafından uydurulamaz.
//  3. EmailJS'in Public Key'i tarayıcıda gizlenemez (tasarımı gereği açıktır). Sunucudan
//     çağırınca Private Key (accessToken) kullanılır ve user-secrets'ta durur — git'e girmez.
//
// EmailJS panelinde tek bir ayar gerekiyor: Account > Security > "Allow EmailJS API for
// non-browser applications" açık olmalı, yoksa sunucudan gelen istekleri reddeder.
public class EmailService
{
    // Normalde EmailJS'in kendi adresi. "EmailJS:ApiUrl" ayarı verilirse oraya gider —
    // bu sayede gerçek mail atmadan (ve gerçek anahtar kullanmadan) yerel bir yakalayıcıya
    // yönlendirip gönderdiğimiz gövdenin doğruluğu test edilebilir.
    private const string VarsayilanUrl = "https://api.emailjs.com/api/v1.0/email/send";
    private string ApiUrl => _config["EmailJS:ApiUrl"] ?? VarsayilanUrl;

    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly AppDbContext _context;
    private readonly ILogger<EmailService> _logger;

    public EmailService(HttpClient http, IConfiguration config, AppDbContext context, ILogger<EmailService> logger)
    {
        _http = http;
        _config = config;
        _context = context;
        _logger = logger;
        _http.Timeout = TimeSpan.FromSeconds(10);   // EmailJS yanıt vermezse sipariş akışını kilitlemesin
    }

    // Tarihler veritabanında UTC tutulur. Tarayıcı UTC'yi kendi saatine çevirebilir (bkz.
    // UtcDateTimeConverter), ama MAİL sunucuda metne dönüşür — çeviriyi burada biz yapmazsak
    // müşteriye "13:51" yazarız, oysa saat 16:51'dir. Bu yüzden Türkiye saatine çevriliyor.
    private static readonly TimeZoneInfo TurkiyeSaati = BulTurkiyeSaatDilimi();

    private static TimeZoneInfo BulTurkiyeSaatDilimi()
    {
        // Windows ve Linux farklı saat dilimi kimlikleri kullanır; ikisini de dene.
        foreach (var id in new[] { "Turkey Standard Time", "Europe/Istanbul" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.Utc;   // Bulunamazsa UTC ile devam et (mail yine gitsin)
    }

    private static string YerelSaatMetni(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TurkiyeSaati)
            .ToString("dd.MM.yyyy HH:mm");

    // Müşteriye gösterilecek durum metni. Enum'ın kendisini maile yazmak ("Shipped") anlamsız olur.
    private static readonly Dictionary<OrderStatus, string> StatusTexts = new()
    {
        [OrderStatus.Pending] = "Siparişiniz alındı",
        [OrderStatus.Approved] = "Siparişiniz onaylandı",
        [OrderStatus.Rejected] = "Siparişiniz reddedildi",
        [OrderStatus.Shipped] = "Siparişiniz kargoya verildi",
        [OrderStatus.Delivered] = "Siparişiniz teslim edildi"
    };

    // Sipariş durumu değiştiğinde çağrılır.
    //
    // ÖNEMLİ: Bu metot ASLA exception fırlatmaz. Mail gönderimi siparişin yan etkisidir;
    // EmailJS'in kotası dolduğu ya da servisi yavaş olduğu için müşterinin siparişi
    // başarısız olmamalı. Hata olursa loglanır ve akış devam eder.
    public async Task SendOrderStatusAsync(int orderId, OrderStatus status)
    {
        int? mailSahibi = null;   // catch bloğunun da loglayabilmesi için dışarıda tutulur
        try
        {
            if (!StatusTexts.TryGetValue(status, out var statusText))
            {
                return;   // İptal gibi durumlarda mail atmıyoruz (müşteri zaten kendi yaptı)
            }

            var ayarlar = ReadSettings();
            if (ayarlar == null)
            {
                // Anahtarlar tanımlı değilse sessizce geç: proje anahtarsız da çalışabilmeli
                _logger.LogWarning("EmailJS ayarları eksik, mail gönderilmedi (sipariş #{OrderId}).", orderId);
                return;
            }

            var order = await _context.Orders
                .AsNoTracking()
                .Include(o => o.User)
                .Include(o => o.OrderItems)
                    .ThenInclude(oi => oi.Product)
                .FirstOrDefaultAsync(o => o.Id == orderId);

            if (order == null) return;
            mailSahibi = order.UserId;

            // E-posta adresi kullanıcının hesabından okunur (bugün eklediğimiz User.Email).
            // Sipariş formunda e-posta toplamıyoruz; toplasaydık istemci başkasının adresini yazabilirdi.
            var aliciMail = order.User?.Email;
            if (string.IsNullOrWhiteSpace(aliciMail))
            {
                _logger.LogInformation("Sipariş #{OrderId}: kullanıcının e-postası yok, mail atlanıyor.", orderId);
                return;
            }

            // Reddedilen kalemler toplama katılmaz (panelde ve sipariş geçmişinde olduğu gibi)
            var gecerliKalemler = order.OrderItems.Where(i => i.Status != OrderStatus.Rejected).ToList();
            var urunListesi = string.Join("\n", gecerliKalemler.Select(i =>
                $"- {i.Product.Name} x{i.Quantity}: {i.UnitPrice * i.Quantity:0.00} TL"));

            var govde = new
            {
                service_id = ayarlar.ServiceId,
                template_id = ayarlar.TemplateId,
                user_id = ayarlar.PublicKey,
                accessToken = ayarlar.PrivateKey,   // Sunucudan çağrı için zorunlu; asıl gizli olan bu
                template_params = new
                {
                    to_email = aliciMail,
                    to_name = order.RecipientName ?? order.User!.Username,
                    order_id = order.Id,
                    order_status = statusText,
                    order_date = YerelSaatMetni(order.CreatedAt),
                    order_items = urunListesi,
                    order_total = $"{gecerliKalemler.Sum(i => i.UnitPrice * i.Quantity):0.00} TL",
                    delivery_address = $"{order.City} / {order.District} — {order.Address}"
                }
            };

            var istek = new StringContent(JsonSerializer.Serialize(govde), Encoding.UTF8, "application/json");
            var cevap = await _http.PostAsync(ApiUrl, istek);

            if (cevap.IsSuccessStatusCode)
            {
                await LogAsync(order.UserId, "Bildirim maili gönderildi", $"Sipariş #{order.Id}: {statusText} → {aliciMail}");
            }
            else
            {
                var hataMetni = await cevap.Content.ReadAsStringAsync();
                _logger.LogError("EmailJS hatası ({Status}): {Body}", cevap.StatusCode, hataMetni);
                await LogAsync(order.UserId, "Bildirim maili gönderilemedi", $"Sipariş #{order.Id}: {cevap.StatusCode} — {hataMetni}");
            }
        }
        catch (Exception ex)
        {
            // Buraya düşmek siparişi etkilemez; sadece müşteri mail almamış olur.
            // Servise hiç ulaşılamaması (bağlantı hatası, zaman aşımı) burada yakalanır —
            // bu durum da panelde görünsün diye Logs tablosuna yazılır, yoksa "mail gelmedi"
            // şikayetinde hiçbir iz kalmazdı.
            _logger.LogError(ex, "Sipariş #{OrderId} için mail gönderilemedi.", orderId);
            await LogAsync(mailSahibi, "Bildirim maili gönderilemedi", $"Sipariş #{orderId}: {ex.Message}");
        }
    }

    // Mail gönderimleri de Logs tablosuna yazılır ("tüm işlemler loglanabilecek" kuralı):
    // müşteri "mail gelmedi" derse panelden gönderilip gönderilmediği görülebilir.
    private async Task LogAsync(int? userId, string action, string details)
    {
        try
        {
            _context.Logs.Add(new Log
            {
                UserId = userId,
                Action = action,
                Details = details,
                Timestamp = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Mail logu yazılamadı.");
        }
    }

    private EmailJsSettings? ReadSettings()
    {
        var s = new EmailJsSettings
        {
            ServiceId = _config["EmailJS:ServiceId"],
            TemplateId = _config["EmailJS:TemplateId"],
            PublicKey = _config["EmailJS:PublicKey"],
            PrivateKey = _config["EmailJS:PrivateKey"]
        };

        bool eksikVar = string.IsNullOrWhiteSpace(s.ServiceId)
            || string.IsNullOrWhiteSpace(s.TemplateId)
            || string.IsNullOrWhiteSpace(s.PublicKey)
            || string.IsNullOrWhiteSpace(s.PrivateKey);

        return eksikVar ? null : s;
    }

    private class EmailJsSettings
    {
        public string? ServiceId { get; set; }
        public string? TemplateId { get; set; }
        public string? PublicKey { get; set; }
        public string? PrivateKey { get; set; }
    }
}
