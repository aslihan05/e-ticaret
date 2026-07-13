using System.Text.Json.Serialization;
namespace ETicaret.Api.Models.Entities;

public class User {
    public int Id {get; set;}
    public string Username { get; set; } = string.Empty;
   
    [JsonIgnore]
    public string PasswordHash {get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }

    public int RoleId {get; set;}     // Veritabanındaki gerçek sütun
    public Role Role {get; set;} = null!;  // navigation: C# tarafında ilişkili
}


// Login panelin karşılığı 
// string.Empty -> Asla null olmaz ,  boş başlar 
//  null!  ->  Şu an null ama EF core dolduracak
// string? -> Bu alan boş kalabilir
// Password değil de PasswordHash olmasının sebebi tabloya açık şifre girelemeyecek olması.
// [JsonIgnore] etiketi -> Bu entity JSON'a çevirilirken PasswordHash alanını her zaman atla demek. 