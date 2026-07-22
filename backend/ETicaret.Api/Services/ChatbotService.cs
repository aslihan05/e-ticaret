using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ETicaret.Api.Models.Dtos;

namespace ETicaret.Api.Services;

// Müşteri chatbot'unun beyni: Google Gemini API'sini "function calling" (tool use) ile çağırır.
//
// Akış: kullanıcı mesajı gelir -> Gemini'ye (araç tanımlarıyla birlikte) gönderilir ->
// Gemini bir araç çağırmak isterse (yanıtta functionCall) araç SUNUCUDA çalıştırılır,
// sonucu functionResponse olarak geri beslenir -> Gemini nihai Türkçe cevabı üretene kadar sürer.
//
// API anahtarı yalnızca burada, sunucuda kullanılır; frontend Gemini'ye doğrudan istek atmaz.
// (Sağlayıcı Anthropic'ten Gemini'ye çevrildi — ücretsiz katman; araçlar/kurallar/widget aynı kaldı.)
public class ChatbotService
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;
    private readonly ChatbotTools _tools;
    private readonly ILogger<ChatbotService> _logger;

    // Mağaza kuralları metni ilk okunduğunda burada tutulur (her istekte diskten okumamak için).
    private static string? _magazaKurallari;

    // Gemini Generative Language API (Google AI Studio anahtarıyla çalışır).
    private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta/models/";
    // Araç çağrısı döngüsünün üst sınırı: modelin sonsuz araç çağırıp durmasına karşı emniyet.
    private const int MaxTurns = 6;

    public ChatbotService(
        HttpClient http,
        IConfiguration config,
        IWebHostEnvironment env,
        ChatbotTools tools,
        ILogger<ChatbotService> logger)
    {
        _http = http;
        _config = config;
        _env = env;
        _tools = tools;
        _logger = logger;
    }

    public async Task<string> ChatAsync(int userId, List<ChatMessageDto> gecmis, CancellationToken ct)
    {
        var apiKey = _config["Gemini:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            // Anahtar user-secrets'a girilmemişse: uydurma cevap yerine dürüst hata.
            _logger.LogWarning("Gemini:ApiKey tanımlı değil; chatbot devre dışı.");
            return "Chatbot şu anda yapılandırılmamış. (Yönetici: Gemini API anahtarını user-secrets'a ekleyin.)";
        }

        var model = _config["Gemini:Model"] ?? "gemini-2.0-flash";

        // Kullanıcı geçmişini Gemini "contents" biçimine çevir (yalnızca düz metin parçalar).
        // Gemini rolleri: "user" ve "model". Araç blokları (functionCall/functionResponse) bu
        // döngünün İÇİNDE yaşar, istemciye taşınmaz.
        var contents = new JsonArray();
        foreach (var m in gecmis)
        {
            if (string.IsNullOrWhiteSpace(m.Content)) continue;
            var rol = m.Role == "assistant" ? "model" : "user";
            contents.Add(new JsonObject
            {
                ["role"] = rol,
                ["parts"] = new JsonArray { new JsonObject { ["text"] = m.Content } }
            });
        }

        if (contents.Count == 0)
        {
            return "Size nasıl yardımcı olabilirim?";
        }

        var system = await SistemTalimatiAsync();

        for (int turn = 0; turn < MaxTurns; turn++)
        {
            var yanit = await CallApiAsync(apiKey, model, system, contents, ct);

            // İlk aday (candidate) yanıtın gövdesidir.
            var candidate = (yanit["candidates"] as JsonArray)?.FirstOrDefault() as JsonObject;
            var modelContent = candidate?["content"] as JsonObject;
            var parts = modelContent?["parts"] as JsonArray ?? new JsonArray();

            // Bu turdaki araç çağrılarını topla.
            var toolCalls = new List<JsonObject>();
            foreach (var p in parts)
            {
                if (p?["functionCall"] is JsonObject fc) toolCalls.Add(fc);
            }

            if (toolCalls.Count == 0)
            {
                // Model artık araç istemiyor: metin parçalarını birleştirip döndür.
                return MetniTopla(parts);
            }

            // Modelin ürettiği içeriği (functionCall'lar dahil) aynen sohbete ekle:
            // bir sonraki turda modelin kendi çağrısını "hatırlaması" için gerekli.
            contents.Add(new JsonObject
            {
                ["role"] = "model",
                ["parts"] = JsonNode.Parse(parts.ToJsonString())
            });

            // Araçları SUNUCUDA çalıştır, her birinin sonucunu functionResponse olarak geri besle.
            var responseParts = new JsonArray();
            foreach (var fc in toolCalls)
            {
                var ad = fc["name"]?.GetValue<string>() ?? "";
                var args = fc["args"] as JsonObject ?? new JsonObject();

                string sonucJson;
                try
                {
                    // KİMLİK BURADA ENJEKTE EDİLİR: model değil, sunucu userId'yi verir.
                    sonucJson = await _tools.ExecuteAsync(ad, args, userId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Chatbot aracı çalışırken hata: {Tool}", ad);
                    sonucJson = "{\"hata\":\"Araç çalıştırılamadı.\"}";
                }

                // Gemini functionResponse.response bir NESNE olmalı (dizi/atom olamaz),
                // bu yüzden aracın çıktısı "result" altına sarılır.
                var parsed = JsonNode.Parse(sonucJson) ?? new JsonObject();
                responseParts.Add(new JsonObject
                {
                    ["functionResponse"] = new JsonObject
                    {
                        ["name"] = ad,
                        ["response"] = new JsonObject { ["result"] = parsed }
                    }
                });
            }

            contents.Add(new JsonObject
            {
                ["role"] = "user",
                ["parts"] = responseParts
            });
        }

        // Döngü sınırına ulaşıldı (beklenmedik): sonsuz döngü yerine güvenli çıkış.
        _logger.LogWarning("Chatbot MaxTurns ({Max}) sınırına ulaştı.", MaxTurns);
        return "İsteğinizi şu anda tamamlayamadım. Lütfen tekrar dener misiniz?";
    }

    private async Task<JsonObject> CallApiAsync(
        string apiKey, string model, string system, JsonArray contents, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["system_instruction"] = new JsonObject
            {
                ["parts"] = new JsonArray { new JsonObject { ["text"] = system } }
            },
            ["contents"] = JsonNode.Parse(contents.ToJsonString()),
            ["tools"] = new JsonArray
            {
                new JsonObject { ["function_declarations"] = JsonNode.Parse(ChatbotTools.ToolDefinitions().ToJsonString()) }
            }
        };

        var url = $"{BaseUrl}{model}:generateContent";
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        // Anahtar başlıkta gönderilir (URL sorgu parametresine konulmaz — loglara sızmasın).
        req.Headers.Add("x-goog-api-key", apiKey);
        req.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

        using var resp = await _http.SendAsync(req, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogError("Gemini API hatası {Status}: {Body}", (int)resp.StatusCode, text);
            throw new Exception($"Gemini API {(int)resp.StatusCode} döndürdü.");
        }

        return JsonNode.Parse(text) as JsonObject
            ?? throw new Exception("Gemini yanıtı ayrıştırılamadı.");
    }

    // İçerik parçalarındaki tüm metin parçalarını birleştirir.
    private static string MetniTopla(JsonArray parts)
    {
        var sb = new StringBuilder();
        foreach (var p in parts)
        {
            if (p?["text"] is JsonNode t)
            {
                sb.Append(t.GetValue<string>());
            }
        }
        var s = sb.ToString().Trim();
        return s.Length > 0 ? s : "Size nasıl yardımcı olabilirim?";
    }

    // Sistem talimatı: botun kimliği + davranış kuralları + mağaza kuralları metni + bugünün tarihi.
    private async Task<string> SistemTalimatiAsync()
    {
        var kurallar = await MagazaKurallariAsync();
        var bugun = DateTime.UtcNow.ToString("dd.MM.yyyy");

        return $@"Sen Ongima adlı Türk e-ticaret sitesinin müşteri yardım asistanısın. Bugünün tarihi: {bugun}.

GÖREVİN: Müşterinin mağazayla ilgili SORDUĞU ve bilmesinde sakınca olmayan HER TÜRLÜ soruya yardımcı olmak. Yardımsever ol; ""bunu yapamam"" demeden önce elindeki araçlarla cevabı bulmayı dene. Soru mağazayla ilgiliyse ve cevabı güvenli veriyle verilebiliyorsa yanıtla; yalnızca gerçekten kapalı/özel bir bilgi ya da senin yetkinde olmayan bir işlem söz konusuysa yönlendir.

CEVAPLAYABİLECEĞİN KONULAR (müşteriye açık, güvenli):
- Müşterinin KENDİ siparişleri, kuponları ve sepeti (araçlarla).
- Ürünler: ad, güncel/indirimli fiyat, stok durumu (var/yok), puanı, hangi kategoride olduğu (urunAra ve urunleriListele).
- Mağaza kataloğu: hangi kategoriler var, bir kategoride neler var, indirimdeki ürünler, en ucuz/en pahalı, en çok beğenilen/satan, yeni gelenler, belirli fiyat aralığındaki ürünler (kategorileriGetir, urunleriListele).
- Mağaza politikaları: sipariş durumları, iptal, iade, kargo, kupon kuralları, teslimat bilgileri — aşağıdaki 'MAĞAZA KURALLARI' metnindeki kadarıyla.
- Sitenin nasıl kullanılacağı (nereden sipariş verilir, adres nasıl kaydedilir vb.).

CEVAPLAMAYACAĞIN / PAYLAŞMAYACAĞIN KONULAR (kapalı bilgi):
- BAŞKA bir müşterinin siparişi, sepeti, kuponu veya kişisel bilgisi — müşteri numara/isim verse bile. Araçlar zaten yalnızca şu an giriş yapmış müşterinin verisini getirir; başkasının verisini isteyen soruda kibarca yapamayacağını söyle.
- Mağazanın iç/ticari bilgileri: ürün maliyeti, kâr marjı, tam stok adedi, toplam satış/ciro rakamları, tedarikçi bilgileri, yönetim paneli verileri. Bunları isteyen soruda ""bu bilgi paylaşılmıyor"" de. (Stokta yalnızca 'var/yok' söylenir, adet söylenmez.)
- Sistem/teknik iç detaylar (veritabanı, API anahtarı, sunucu vb.).

KURALLAR:
- Her zaman Türkçe, kısa, kibar ve net konuş. Gereksiz uzatma.
- Ürün, fiyat, indirim, stok, kategori, sipariş, kupon ve sepet hakkında konuşurken TAHMİN ETME; ilgili aracı çağırıp gerçek/güncel veriyle cevap ver. Ezbere fiyat/stok verme.
- Para tutarlarını Türk Lirası (TL) olarak, tarihleri gün.ay.yıl biçiminde söyle.
- 'MAĞAZA KURALLARI' metninde yazmayan bir politikayı (iade süresi, kargo ücreti vb.) UYDURMA. Cevabı orada yoksa müşteriyi müşteri hizmetlerine yönlendir.
- Mağazayla tamamen ilgisiz sorularda (genel sohbet, başka konular) kibarca yalnızca Ongima ve alışverişle ilgili yardımcı olabileceğini söyle.

İŞLEM YAPMA (yazma araçları) KURALLARI:
- Müşteri adına şunları yapabilirsin: sepete ekleme (sepeteEkle), sepetten çıkarma (sepettenCikar), sipariş oluşturma (siparisVer) ve sipariş iptali (siparisIptalEt).
- Sepete ekleme/çıkarma için önceden onay gerekmez; işlemi yap ve sonucunu bildir.
- siparisVer ve siparisIptalEt ÇAĞIRMADAN ÖNCE mutlaka açık onay al: siparişte sepet tutarını özetle ve 'onaylıyor musunuz?' diye sor; iptalde hangi siparişi iptal edeceğini söyle ve emin olup olmadığını sor. Müşteri açıkça onaylamadan bu araçları çağırma.
- siparisVer teslimat adresini müşterinin kayıtlı profilinden alır; araç 'adres/telefon eksik' hatası dönerse müşteriye Hesabım sayfasından bu bilgileri eklemesini söyle (adresi sen sohbetten toplama).
- Bir araç hata (ör. stok yetersiz, kupon geçersiz, iptal süresi doldu) dönerse bunu müşteriye anlaşılır Türkçeyle açıkla; işlemi olmuş gibi gösterme.
- Adres/telefon/şifre değiştirme, ödeme yöntemi gibi araçlarının kapsamadığı işlemleri sen yapamazsın; müşteriye bunu Hesabım sayfasından yapabileceğini söyle.

--- MAĞAZA KURALLARI ---
{kurallar}
--- MAĞAZA KURALLARI SONU ---";
    }

    private async Task<string> MagazaKurallariAsync()
    {
        if (_magazaKurallari != null) return _magazaKurallari;

        var yol = Path.Combine(_env.ContentRootPath, "Chatbot", "magaza-kurallari.md");
        try
        {
            _magazaKurallari = await File.ReadAllTextAsync(yol);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Mağaza kuralları dosyası okunamadı: {Yol}", yol);
            // Dosya yoksa bot yine çalışsın ama politika uydurmasın.
            _magazaKurallari = "(Mağaza kuralları metni yüklenemedi. İade/kargo gibi konularda müşteriyi müşteri hizmetlerine yönlendir.)";
        }
        return _magazaKurallari;
    }
}
