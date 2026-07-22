namespace ETicaret.Api.Models.Dtos;

public class UserSummaryDto
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string Role { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public int SortOrder { get; set; }
    public bool IsBlocked { get; set; }
}