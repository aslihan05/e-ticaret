namespace ETicaret.Api.Models.Dtos;

// Gizemli Hediye Kutuları oyunlaştırma modülünün istemciye dönen tipleri.
//
// Önemli güvenlik notu: ödül (Won) HER ZAMAN sunucuda belirlenir ve kupon sunucuda
// oluşturulup kullanıcıya atanır. İstemci hangi kutuya tıkladığını gönderse bile bu
// kararı etkilemez — "kaçırılan" ödüllerin (Missed) kod alanı boştur, yani müşteri
// yalnızca kazandığı kuponu kullanabilir (client-side hilesine kapalı).

// Tek bir ödülün gösterim bilgisi.
public class MysteryPrizeDto
{
    public string Emoji { get; set; } = "";
    public string Label { get; set; } = "";
    public string Summary { get; set; } = "";          // "%20" ya da "40 TL"
    public decimal? MinOrderTotal { get; set; }         // varsa alt sepet limiti (bilgi amaçlı)

    // Yalnızca KAZANILAN ödülde doldurulur; kaçırılan kutularda null kalır.
    public string? Code { get; set; }
    public DateTime? ExpiresAt { get; set; }
}

// POST /api/mysterybox/open cevabı.
public class MysteryBoxResultDto
{
    public bool AlreadyPlayed { get; set; }             // bugün zaten oynadıysa true
    public MysteryPrizeDto? Won { get; set; }           // kazanılan ödül (kupon koduyla)
    public List<MysteryPrizeDto> Missed { get; set; } = new();   // seçilmeyen 2 kutu
    public DateTime? NextAvailableAt { get; set; }      // yeniden oynanabilecek an
    public string Message { get; set; } = "";
}

// GET /api/mysterybox cevabı — sayfa açılırken kutuların açık mı yoksa
// "yarın tekrar gel" mi gösterileceğine bu karar verir.
public class MysteryBoxStatusDto
{
    public bool CanOpen { get; set; }
    public DateTime? NextAvailableAt { get; set; }
    public MysteryPrizeDto? LastPrize { get; set; }     // bugün kazanılmışsa o ödül

    // Oyunun kendisi admin tarafından kapatılmış mı? Bu durumda "yarın gel" değil
    // "şu an kapalı" ekranı gösterilir (NextAvailableAt de boş gelir).
    public bool GameClosed { get; set; }

    // Ekranda kaç kutu çizilecek. Admin ayarlayabilir; havuzdaki aktif ödül sayısından
    // fazla olamaz, o yüzden karar sunucuda verilir.
    public int BoxCount { get; set; } = 3;
}
