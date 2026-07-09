namespace ETicaret.Api.Models.Entities;

public class Log
{
    public int Id { get; set; }

    public int? UserId { get; set; }
    public User? User { get; set; }

    public string Action { get; set; } = string.Empty;
    public string? Details { get; set; }

    public DateTime Timestamp { get; set; }
}
