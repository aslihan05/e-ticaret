using System.Text.Json.Serialization;
namespace ETicaret.Api.Models.Entities;

public class User {
    public int Id {get; set;}
    public string Username { get; set; } = string.Empty;
   
    [JsonIgnore]
    public string PasswordHash {get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }

    public int RoleId {get; set;}
    public Role Role {get; set;} = null!;
}