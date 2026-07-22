namespace ETicaret.Api.Models.Dtos;

// İl (il) seviyesindeki özet: kaç sipariş, toplam ciro, kaç ilçe.
// Ham siparişler taşınmaz; yalnızca SQL'de hesaplanan bu hafif özet döner.
public class CityStatDto
{
    public string City { get; set; } = string.Empty;
    public int OrderCount { get; set; }
    public decimal TotalRevenue { get; set; }
    public int DistrictCount { get; set; }
}

// İlçe seviyesindeki özet: bir ile tıklanınca (drill-down) yüklenir.
public class CityDistrictStatDto
{
    public string City { get; set; } = string.Empty;
    public string District { get; set; } = string.Empty;
    public int OrderCount { get; set; }
    public decimal TotalRevenue { get; set; }
}
