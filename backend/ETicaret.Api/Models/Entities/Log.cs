namespace ETicaret.Api.Models.Entities;

// Log kaydının önem derecesi. Panelde renkli rozetle gösterilir, ayrıca
// "sadece hataları göster" gibi filtrelemeye imkân verir.
public enum LogSeverity
{
    Info,     // Normal işlem (sipariş oluşturuldu, ürün güncellendi...)
    Warning,  // Beklenen ama başarısız istek (doğrulama hatası, 4xx)
    Error     // Yakalanmamış istisna / 5xx — stack trace ile birlikte tutulur
}

// Eskiden Log yalnızca "kim / ne / ne zaman" tutuyordu. Bir arıza araştırılırken
// bu yetmiyordu: hangi IP'den geldi, gövdede ne vardı, hata nerede patladı?
// Bu alanlar o soruların cevabını tek satırda toplar. Büyük metin alanları
// (gövdeler, stack trace) nullable ve string olduğu için SQL Server'da
// nvarchar(max) olarak açılır — kısa kayıtlarda yer kaplamaz, uzun kayıtta taşmaz.
public class Log
{
    public int Id { get; set; }

    // ===== Kim =====
    public int? UserId { get; set; }
    public User? User { get; set; }
    public string? Username { get; set; }   // İsteğin geldiği andaki ad; kullanıcı sonradan silinse de log okunur kalsın

    // ===== Ne =====
    public string Action { get; set; } = string.Empty;   // Okunabilir işlem adı ("Sipariş oluşturuldu")
    public string? Details { get; set; }                 // Kısa ek açıklama
    public LogSeverity Level { get; set; } = LogSeverity.Info;

    // ===== İstek bağlamı =====
    public string? HttpMethod { get; set; }    // GET / POST / PUT / DELETE
    public string? Path { get; set; }          // /api/orders/12/cancel
    public int? StatusCode { get; set; }       // 200 / 400 / 500
    public string? IpAddress { get; set; }     // İsteğin geldiği IP
    public string? UserAgent { get; set; }     // Tarayıcı/istemci bilgisi
    public long? DurationMs { get; set; }       // İsteğin sürdüğü süre (ms)

    // ===== Gövdeler (JSON) =====
    // Şifre gibi hassas alanlar yazılmadan önce maskelenir (bkz. LoggingMiddleware).
    public string? RequestBody { get; set; }
    public string? ResponseBody { get; set; }

    // ===== Hata =====
    public string? Exception { get; set; }     // İstisnanın mesajı ve tipi
    public string? StackTrace { get; set; }    // Hatanın oluştuğu çağrı yığını

    public DateTime Timestamp { get; set; }
}
