using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ETicaret.Api.Data;
using ETicaret.Api.Models.Entities;

namespace ETicaret.Api.Middleware;

// Tek bir middleware iki işi birden görür:
//   1) Global Exception Handler: _next içinde patlayan HER istisnayı yakalar, stack trace'iyle
//      birlikte loglar ve istemciye sızıntısız bir 500 döner (ham hata mesajı/stack trace
//      tarayıcıya gitmez).
//   2) Ayrıntılı işlem logu: kim, hangi IP'den, hangi metotla, ne kadar sürede, hangi gövdeyle
//      ne yaptı — hepsi tek kayıtta. Başarılı okuma (GET) istekleri gürültü yapmasın diye
//      atlanır; ama GET bile olsa HATA her zaman loglanır.
public class LoggingMiddleware
{
    private readonly RequestDelegate _next;
    // Log yazımı için isteğe ait scoped AppDbContext'i KULLANMIYORUZ: bir istek DB hatasıyla
    // patladıysa o context "kirli" olabilir (bekleyen hatalı değişiklikler). Temiz bir scope
    // açıp logu ayrı bir context'le yazmak, hata anında da logun güvenle kaydını sağlar.
    private readonly IServiceScopeFactory _scopeFactory;

    // Gövdeler kısılır: dev bir base64 görsel ya da yüzlerce kalemlik yanıt logu şişirmesin.
    private const int MaxBodyLength = 4000;

    public LoggingMiddleware(RequestDelegate next, IServiceScopeFactory scopeFactory)
    {
        _next = next;
        _scopeFactory = scopeFactory;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var sw = Stopwatch.StartNew();

        // İstek gövdesini _next'ten SONRA da okuyabilmek için buffering aç ve şimdi oku
        // (controller gövdeyi tükettikten sonra stream başa sarılamazdı).
        string requestBody = await ReadRequestBodyAsync(context.Request);

        // Yanıt gövdesini görebilmek için gerçek stream'i geçici bir bellek akışıyla değiştiriyoruz.
        // İstemciye henüz hiçbir şey yazılmadığı için (buffer bellekte) hata anında Response.Clear()
        // ile temiz bir 500 üretmek de mümkün olur.
        var originalBody = context.Response.Body;
        using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        Exception? error = null;
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            error = ex;
            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.WriteAsync(
                JsonSerializer.Serialize(new { message = "Sunucuda beklenmeyen bir hata oluştu." }));
        }
        sw.Stop();

        // Yanıtı logla-oku, sonra gerçek stream'e geri kopyala (istemci yanıtını alsın)
        buffer.Position = 0;
        string responseBody = await ReadStreamCappedAsync(buffer);
        buffer.Position = 0;
        await buffer.CopyToAsync(originalBody);
        context.Response.Body = originalBody;

        await WriteLogAsync(context, requestBody, responseBody, sw.ElapsedMilliseconds, error);
    }

    private async Task WriteLogAsync(HttpContext context, string requestBody, string responseBody, long durationMs, Exception? error)
    {
        string method = context.Request.Method;
        string path = context.Request.Path.Value ?? "";
        string pathLower = path.ToLower();
        int status = context.Response.StatusCode;

        bool isError = error != null || status >= 500;
        bool isClientError = status >= 400 && status < 500;

        // Başarılı istekler için gürültü elemesi: GET okumaları ve (giriş/kayıt gibi) başka
        // yerde ayrıntılı loglanan yollar atlanır. HATA/uyarı durumları bu elemeye takılmaz.
        if (!isError && !isClientError)
        {
            if (method == "GET") return;
            if (pathLower.StartsWith("/api/auth")) return;   // AuthService ayrıntılı logluyor
            if (pathLower.StartsWith("/api/products")) return; // ProductsController "hangi ürün" bilgisini ayrıntılı logluyor
            if (pathLower.Contains("/discount")) return;      // indirimler controller'da loglanıyor
        }

        var level = isError ? LogSeverity.Error
                  : isClientError ? LogSeverity.Warning
                  : LogSeverity.Info;

        int? userId = null;
        var userIdClaim = context.User.FindFirst("UserId");
        if (userIdClaim != null && int.TryParse(userIdClaim.Value, out int uid))
        {
            userId = uid;
        }

        var log = new Log
        {
            UserId = userId,
            Username = context.User.Identity?.Name,
            Action = isError ? $"HATA: {FriendlyAction(method, pathLower)}" : FriendlyAction(method, pathLower),
            Details = BuildDetails(context, method, path, pathLower, status, durationMs, error),
            Level = level,
            HttpMethod = method,
            Path = path,
            StatusCode = status,
            IpAddress = context.Connection.RemoteIpAddress?.ToString(),
            UserAgent = context.Request.Headers.UserAgent.ToString() is { Length: > 0 } ua ? ua : null,
            DurationMs = durationMs,
            RequestBody = Redact(requestBody),
            ResponseBody = string.IsNullOrWhiteSpace(responseBody) ? null : responseBody,
            Exception = error != null ? $"{error.GetType().Name}: {error.Message}" : null,
            StackTrace = Cap(error?.StackTrace),
            Timestamp = DateTime.UtcNow
        };

        try
        {
            // Temiz, isteğe bağlı olmayan bir context ile yaz (yukarıdaki gerekçe).
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Logs.Add(log);
            await db.SaveChangesAsync();
        }
        catch
        {
            // Log yazımının kendisi isteği düşürmemeli — sessizce geç.
        }
    }

    // ===== Yardımcılar =====

    private static async Task<string> ReadRequestBodyAsync(HttpRequest request)
    {
        // Sadece gövdesi anlamlı olan metotlarda oku
        if (request.ContentLength is null or 0) return "";
        if (!(request.Method == "POST" || request.Method == "PUT" || request.Method == "PATCH")) return "";

        request.EnableBuffering();
        request.Body.Position = 0;
        using var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true);
        var text = await reader.ReadToEndAsync();
        request.Body.Position = 0;   // controller'ın tekrar okuyabilmesi için başa sar
        return Cap(text) ?? "";
    }

    private static async Task<string> ReadStreamCappedAsync(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        var text = await reader.ReadToEndAsync();
        return Cap(text) ?? "";
    }

    private static string? Cap(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        return text.Length <= MaxBodyLength ? text : text[..MaxBodyLength] + "…(kısaltıldı)";
    }

    // Şifre ve token gibi hassas alanların değerini gövdeden maskeler. Auth başarılı akışı
    // zaten loglanmıyor, ama başarısız giriş (4xx) gövdesi ya da beklenmeyen bir hata,
    // düz şifreyi log tablosuna düşürebilirdi — bu buna izin vermez.
    private static readonly Regex SensitiveRegex = new(
        "\"(password|passwordHash|currentPassword|newPassword|token|accessToken)\"\\s*:\\s*\"[^\"]*\"",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static string? Redact(string? body)
    {
        if (string.IsNullOrEmpty(body)) return body;
        return SensitiveRegex.Replace(body, m => m.Value.Split(':')[0] + ":\"***\"");
    }

    // "Açıklama" sütununu tek, düz Türkçe bir cümleye dönüştürür:
    //   kim — nereden — ne yaptı — hangi sonuçla — ne kadar sürdü.
    // Böylece panele bakan biri ham "POST /api/orders" görmek yerine olayı okur gibi anlar.
    // Örn: "Ahmet kullanıcısı 192.168.1.5 IP'sinden sipariş oluşturdu; işlem başarıyla
    //       tamamlandı (200), 45 ms sürdü."
    private static string BuildDetails(HttpContext context, string method, string path, string pathLower, int status, long durationMs, Exception? error)
    {
        // Kim
        string? username = context.User.Identity?.Name;
        string kim = string.IsNullOrWhiteSpace(username) ? "Bir ziyaretçi" : $"{username} kullanıcısı";

        // Nereden
        string? ip = context.Connection.RemoteIpAddress?.ToString();
        string nereden = string.IsNullOrWhiteSpace(ip) ? "" : $"{ip} IP'sinden ";

        // Ne yaptı — okunabilir işlem adını cümleye uygun fiile çevir ("Sipariş oluşturuldu"
        // → "sipariş oluşturmayı denedi/oluşturdu" yerine sade tutuyoruz: eylemi küçük harfle).
        string eylem = FiilCumlesi(method, pathLower);

        // Sonuç — durum kodunun düz Türkçe karşılığı
        string sonuc = SonucMetni(status, error);

        return $"{kim} {nereden}{eylem}; işlem {sonuc} ({status}), {durationMs} ms sürdü.";
    }

    // İşlem adını bir cümlenin ortasına oturacak küçük harfli eylem ifadesine çevirir.
    private static string FiilCumlesi(string method, string path)
    {
        string ad = FriendlyAction(method, path);
        // "METHOD /yol" gibi genelleştirilemeyen kayıtlarda ham yolu koru.
        if (ad.StartsWith(method + " ")) return $"{method} {path} isteği gönderdi";
        // Baş harfi küçült: "Sipariş oluşturuldu" → "sipariş oluşturuldu".
        return char.ToLower(ad[0]) + ad[1..];
    }

    // Durum kodunu "neden" bilgisi taşıyan düz Türkçe bir sonuç ifadesine çevirir.
    private static string SonucMetni(int status, Exception? error)
    {
        if (error != null || status >= 500) return "sunucu hatası nedeniyle başarısız oldu";
        return status switch
        {
            >= 200 and < 300 => "başarıyla tamamlandı",
            400 => "geçersiz veri nedeniyle reddedildi",
            401 => "oturum/yetki doğrulanamadığı için reddedildi",
            403 => "yetki yetersizliğinden reddedildi",
            404 => "ilgili kayıt bulunamadığı için tamamlanamadı",
            409 => "çakışma (ör. zaten var / durum uygun değil) nedeniyle reddedildi",
            >= 400 and < 500 => "geçersiz istek olarak reddedildi",
            _ => "tamamlandı"
        };
    }

    // Yol + metottan okunabilir Türkçe işlem adı üretir
    private static string FriendlyAction(string method, string path)
    {
        if (path.StartsWith("/api/products"))
        {
            if (path.EndsWith("/stock")) return "Stok güncellendi";
            if (path.EndsWith("/activate")) return "Ürün aktifleştirildi";
            if (method == "POST") return "Ürün eklendi";
            if (method == "PUT") return "Ürün güncellendi";
            if (method == "DELETE") return "Ürün pasife alındı";
        }
        else if (path.StartsWith("/api/categories"))
        {
            if (method == "POST") return "Kategori eklendi";
            if (method == "PUT") return "Kategori güncellendi";
            if (method == "DELETE") return "Kategori silindi";
        }
        else if (path.StartsWith("/api/admin/users"))
        {
            if (method == "POST") return "Kullanıcı eklendi";
            if (method == "PUT") return "Kullanıcı güncellendi";
            if (method == "DELETE") return "Kullanıcı silindi";
        }
        else if (path.StartsWith("/api/admin/orders"))
        {
            if (path.EndsWith("/approve")) return "Sipariş onaylandı";
            if (path.EndsWith("/reject")) return "Sipariş reddedildi";
            if (path.EndsWith("/decide")) return "Sipariş kararı verildi";
            if (path.EndsWith("/status")) return "Sipariş durumu güncellendi";
            if (method == "DELETE") return "Sipariş silindi";
        }
        else if (path.StartsWith("/api/orders"))
        {
            if (path.EndsWith("/cancel")) return "Sipariş iptal edildi";
            if (method == "POST") return "Sipariş oluşturuldu";
        }
        else if (path.StartsWith("/api/cart"))
        {
            if (method == "POST") return "Sepete ürün eklendi";
            if (method == "PUT") return "Sepetteki adet güncellendi";
            if (method == "DELETE") return "Sepetten ürün çıkarıldı";
        }

        return $"{method} {path}";
    }
}
