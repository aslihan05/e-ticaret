namespace ETicaret.Api.Models.Dtos;

// Tek bir filtre kuralı. Frontend her sütun için (metin kutusu, tarih seçici, dropdown,
// min/max) bu şekilde bir kural üretir; hepsi tek bir listede toplanıp gönderilir.
//
// Field  : Entity üzerindeki alan adı. Nokta ile navigasyon desteklenir ("Category.Name",
//          "Product.Name", "Order.User.Username").
// Op     : Operatör. Alanın tipine göre yorumlanır:
//            contains | eq | neq            (metin)
//            eq | neq                        (bool / enum)
//            eq | gt | gte | lt | lte        (sayı)
//            from | to | gte | lte | eq      (tarih — from: gün başı≥, to: gün sonu≤)
//          Boş bırakılırsa alanın tipine uygun makul bir varsayılan seçilir
//          (metin→contains, sayı/enum→eq, tarih→gte).
// Value  : Aranan değer (her zaman string; sunucuda alanın tipine çevrilir).
public class FilterRule
{
    public string? Field { get; set; }
    public string? Op { get; set; }
    public string? Value { get; set; }
}

// Çoklu-kolon dinamik filtre isteği: birden çok kural + isteğe bağlı sıralama.
// Aynı DTO tüm entity'ler (Ürün, Sipariş, Ürün Geçmişi...) için kullanılır.
public class FilterRequest
{
    public List<FilterRule>? Filters { get; set; }
    public string? SortBy { get; set; }
    public string? SortDir { get; set; }   // asc | desc
}
