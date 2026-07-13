namespace ETicaret.Api.Models.Entities;

public class Log
{
    public int Id { get; set; }

    public int? UserId { get; set; }  // Kim
    public User? User { get; set; }

    public string Action { get; set; } = string.Empty;  // Ne yaptı
    public string? Details { get; set; } // Ayrıntı

    public DateTime Timestamp { get; set; }  // Ne zaman
}
