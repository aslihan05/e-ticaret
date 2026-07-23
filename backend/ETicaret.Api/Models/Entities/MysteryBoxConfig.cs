namespace ETicaret.Api.Models.Entities;

// Gizemli hediye kutusu oyununun tekil (singleton) ayar kaydı — tek satır (Id = 1) olur.
// Önceden bu değerler koda gömülü sabitlerdi (24 saat bekleme, 7 gün geçerlilik);
// artık admin panelinden ayarlanabilir.
public class MysteryBoxConfig
{
    public int Id { get; set; }

    // false ise oyun kapalıdır: kutular açılmaz, müşteriye "şu an kapalı" denir.
    public bool IsActive { get; set; } = true;

    // İki oyun arasında beklenecek süre (saat). Deneme-yanılmayla daha iyi ödül avlamayı
    // engelleyen kural; sunucuda zorlanır.
    public int CooldownHours { get; set; } = 24;

    // Kazanılan kuponun kaç gün geçerli olacağı. Süresiz bırakmak, oynanan her gün için
    // biriken ölü kupon yığını demekti.
    public int CouponValidDays { get; set; } = 7;

    // Ekranda kaç kutu gösterileceği (kazanan 1, kaçırılan diğerleri). Havuzda daha az
    // aktif ödül varsa kutu sayısı ona düşer.
    public int BoxCount { get; set; } = 3;
}
