using System.Text.Json.Serialization;
namespace ETicaret.Api.Models.Entities;

public class User {
    public int Id {get; set;}
    public string Username { get; set; } = string.Empty;
    public string? Email { get; set; }

    // İletişim ve teslimat bilgileri: sipariş sırasında teslimat formuna ön değer olur.
    // Sipariş üzerindeki Phone/Address/City/District'ten farklıdır — oradaki, sipariş anındaki
    // anlık görüntüdür ve kullanıcı sonradan bilgisini değiştirse bile eski sipariş sabit kalır.
    //
    // İl/ilçe de burada tutulur: müşteri adresini bir kez girsin, her siparişte tekrar
    // yazmasın diye. Adresin parçaları ayrı sütunlarda çünkü sipariş tablosunda da öyle.
    //
    // FullName, Username'den ayrı: Username giriş kimliğidir ("aslihan34"), kargo etiketine
    // yazılacak ad değil. Boşsa sipariş formunda alıcı adı yine sorulur.
    public string? FullName { get; set; }
    public string? Phone { get; set; }
    public string? City { get; set; }
    public string? District { get; set; }
    public string? Address { get; set; }

    [JsonIgnore]
    public string PasswordHash {get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }

    // Admin panelindeki Kullanıcılar listesinde manuel sıralama için (küçük önce gösterilir)
    public int SortOrder { get; set; }

    // true ise kullanıcı giriş yapamaz (admin tarafından engellenmiş)
    public bool IsBlocked { get; set; }

    public int RoleId {get; set;}     // Veritabanındaki gerçek sütun
    public Role Role {get; set;} = null!;  // navigation: C# tarafında ilişkili
}


// Login panelin karşılığı 
// string.Empty -> Asla null olmaz ,  boş başlar 
//  null!  ->  Şu an null ama EF core dolduracak
// string? -> Bu alan boş kalabilir
// Password değil de PasswordHash olmasının sebebi tabloya açık şifre girelemeyecek olması.
// [JsonIgnore] etiketi -> Bu entity JSON'a çevirilirken PasswordHash alanını her zaman atla demek. 