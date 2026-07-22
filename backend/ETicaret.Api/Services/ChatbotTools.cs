using System.Text.Json;
using System.Text.Json.Nodes;
using ETicaret.Api.Data;
using ETicaret.Api.Models.Dtos;
using ETicaret.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace ETicaret.Api.Services;

// Chatbot'un çağırabileceği "okuma" araçları.
//
// GÜVENLİK — pazarlığa kapalı: Araçların HİÇBİRİ kullanıcı kimliğini parametre olarak
// ALMAZ. Kimlik her zaman ExecuteAsync'e dışarıdan (imzalı JWT'den okunmuş CurrentUserId)
// verilir. Model "3 numaralı kullanıcının siparişlerini getir" diyemez; yalnızca "kendi
// siparişlerimi getir" diyebilir. Aksi halde müşteri sohbet kutusuna id yazarak başkasının
// verisine ulaşırdı (IDOR).
public class ChatbotTools
{
    private readonly AppDbContext _context;
    private readonly StockService _stockService;
    private readonly OrderService _orderService;

    public ChatbotTools(AppDbContext context, StockService stockService, OrderService orderService)
    {
        _context = context;
        _stockService = stockService;
        _orderService = orderService;
    }

    // Model tarafına verilecek araç tanımları — Gemini "function_declarations" biçimi.
    // Araçlar bilinçli olarak parametresizdir (urunAra hariç): kimlik gibi bir girdi almazlar.
    // Parametresiz araçlarda "parameters" alanı hiç verilmez (Gemini boş şemayı istemez).
    public static JsonArray ToolDefinitions() => new JsonArray
    {
        new JsonObject
        {
            ["name"] = "siparislerimiGetir",
            ["description"] = "Giriş yapmış müşterinin KENDİ sipariş geçmişini döndürür: "
                + "sipariş numarası, durumu, tarihi, tutarı, kalemleri ve iptal edilebilir olup olmadığı. "
                + "Müşteri 'siparişim nerede', 'kargoya verildi mi', 'siparişimi iptal edebilir miyim' gibi "
                + "sorular sorduğunda kullan."
        },
        new JsonObject
        {
            ["name"] = "kuponlarimiGetir",
            ["description"] = "Giriş yapmış müşterinin ŞU AN kullanabileceği kuponları döndürür: "
                + "kod, indirim türü/oranı, alt sepet limiti, son kullanma tarihi ve kalan kullanım hakkı. "
                + "Müşteri 'kuponum var mı', 'hangi indirimleri kullanabilirim' diye sorduğunda kullan."
        },
        new JsonObject
        {
            ["name"] = "sepetimiGetir",
            ["description"] = "Giriş yapmış müşterinin sepetindeki ürünleri ve sepet ara toplamını döndürür. "
                + "Müşteri 'sepetimde ne var', 'sepetim ne kadar tuttu' diye sorduğunda kullan."
        },
        new JsonObject
        {
            ["name"] = "urunAra",
            ["description"] = "Mağazadaki satıştaki ürünlerde isme göre arama yapar; ad, fiyat "
                + "(indirim varsa indirimli fiyat) ve stok durumu döner. Müşteri bir ürünü, fiyatını "
                + "veya stokta olup olmadığını sorduğunda kullan.",
            ["parameters"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["arama"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "Aranacak ürün adı veya anahtar kelime."
                    }
                },
                ["required"] = new JsonArray { "arama" }
            }
        },
        new JsonObject
        {
            ["name"] = "kategorileriGetir",
            ["description"] = "Mağazadaki tüm kategorileri (ana kategoriler ve alt kategorileri) "
                + "ve her birindeki satıştaki ürün sayısını döndürür. Müşteri 'hangi kategorileriniz "
                + "var', 'neler satıyorsunuz', 'ürünler nasıl gruplanmış' gibi sorular sorduğunda kullan."
        },
        new JsonObject
        {
            ["name"] = "urunleriListele",
            ["description"] = "Mağazadaki satıştaki ürünleri kategoriye, fiyat aralığına ve indirim "
                + "durumuna göre süzüp sıralı biçimde döndürür (ilk 10). İsimle arama YAPMAZ (onun için "
                + "urunAra var); bunun yerine 'keşif' sorularına cevap verir: 'hangi ürünler indirimde', "
                + "'en ucuz/en pahalı ürünler', 'en çok beğenilenler', 'en çok satanlar', 'yeni gelenler', "
                + "'spor kategorisinde neler var', '100-300 TL arası ürünler' gibi. Tüm parametreler "
                + "isteğe bağlıdır; hiçbiri verilmezse öne çıkan ürünleri getirir.",
            ["parameters"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["kategori"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "Kategori adı (alt kategorileri de kapsar). Örn: 'Giyim', 'Elektronik'."
                    },
                    ["enAzFiyat"] = new JsonObject
                    {
                        ["type"] = "number",
                        ["description"] = "Alt fiyat sınırı (TL). Güncel/ indirimli fiyata göre süzer."
                    },
                    ["enCokFiyat"] = new JsonObject
                    {
                        ["type"] = "number",
                        ["description"] = "Üst fiyat sınırı (TL). Güncel/ indirimli fiyata göre süzer."
                    },
                    ["sadeceIndirimli"] = new JsonObject
                    {
                        ["type"] = "boolean",
                        ["description"] = "true verilirse yalnızca indirimi aktif ürünleri getirir."
                    },
                    ["sirala"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "Sıralama: 'fiyatArtan', 'fiyatAzalan', 'enCokBegenilen', "
                            + "'enCokSatan', 'yeniGelenler'. Verilmezse öne çıkanlar (favori+puan) sırasıyla gelir."
                    }
                }
            }
        },
        // ===== YAZMA araçları =====
        new JsonObject
        {
            ["name"] = "sepeteEkle",
            ["description"] = "Giriş yapmış müşterinin sepetine ürün ekler. Ürün adıyla eşleşen "
                + "tek bir satıştaki ürün bulunursa eklenir; stok yetersizse veya birden fazla ürün "
                + "eşleşirse bilgilendirir. Geri alınabilir bir işlemdir (müşteri onaylamadan sipariş oluşmaz).",
            ["parameters"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["urunAdi"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "Sepete eklenecek ürünün adı."
                    },
                    ["adet"] = new JsonObject
                    {
                        ["type"] = "integer",
                        ["description"] = "Eklenecek adet (verilmezse 1)."
                    }
                },
                ["required"] = new JsonArray { "urunAdi" }
            }
        },
        new JsonObject
        {
            ["name"] = "sepettenCikar",
            ["description"] = "Giriş yapmış müşterinin sepetinden, adıyla eşleşen ürünü tamamen çıkarır. "
                + "Geri alınabilir (müşteri tekrar ekleyebilir).",
            ["parameters"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["urunAdi"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "Sepetten çıkarılacak ürünün adı."
                    }
                },
                ["required"] = new JsonArray { "urunAdi" }
            }
        },
        new JsonObject
        {
            ["name"] = "siparisVer",
            ["description"] = "Giriş yapmış müşterinin sepetindeki ürünlerden sipariş oluşturur. Teslimat "
                + "bilgisi müşterinin KAYITLI profil adresinden alınır (adres/telefon eksikse hata döner, "
                + "müşteri Hesabım sayfasından tamamlamalı). ÖNEMLİ: Bu aracı çağırmadan ÖNCE müşteriye "
                + "sepet tutarını özetleyip açık onay al ('siparişi onaylıyor musunuz?'). Onay alınmadan çağırma.",
            ["parameters"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["kuponKodu"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "İsteğe bağlı kupon kodu. Müşteri kupon kullanmak istemiyorsa boş bırak."
                    }
                }
            }
        },
        new JsonObject
        {
            ["name"] = "siparisIptalEt",
            ["description"] = "Giriş yapmış müşterinin KENDİ siparişini iptal eder. Yalnızca 'Bekliyor' "
                + "durumundaki ve verildikten sonraki 1 saat içindeki siparişler iptal edilebilir (kuralı "
                + "sunucu zorlar; başka birinin siparişi iptal edilemez). ÖNEMLİ: İptal geri alınamaz — bu "
                + "aracı çağırmadan ÖNCE müşteriye hangi siparişi iptal edeceğini söyleyip açık onay al.",
            ["parameters"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["siparisNo"] = new JsonObject
                    {
                        ["type"] = "integer",
                        ["description"] = "İptal edilecek siparişin numarası."
                    }
                },
                ["required"] = new JsonArray { "siparisNo" }
            }
        }
    };

    // Model bir aracı çağırdığında buraya düşer. userId DIŞARIDAN (JWT) gelir, modelden değil.
    public async Task<string> ExecuteAsync(string toolName, JsonObject input, int userId)
    {
        return toolName switch
        {
            "siparislerimiGetir" => await SiparislerimiGetirAsync(userId),
            "kuponlarimiGetir" => await KuponlarimiGetirAsync(userId),
            "sepetimiGetir" => await SepetimiGetirAsync(userId),
            "urunAra" => await UrunAraAsync(input["arama"]?.GetValue<string>() ?? ""),
            "kategorileriGetir" => await KategorileriGetirAsync(),
            "urunleriListele" => await UrunleriListeleAsync(
                input["kategori"]?.GetValue<string>(),
                DecimalAl(input["enAzFiyat"]),
                DecimalAl(input["enCokFiyat"]),
                BoolAl(input["sadeceIndirimli"]),
                input["sirala"]?.GetValue<string>()),
            "sepeteEkle" => await SepeteEkleAsync(userId,
                input["urunAdi"]?.GetValue<string>() ?? "", IntAl(input["adet"]) ?? 1),
            "sepettenCikar" => await SepettenCikarAsync(userId, input["urunAdi"]?.GetValue<string>() ?? ""),
            "siparisVer" => await SiparisVerAsync(userId, input["kuponKodu"]?.GetValue<string>()),
            "siparisIptalEt" => await SiparisIptalEtAsync(userId, IntAl(input["siparisNo"]) ?? 0),
            // Bilinmeyen araç: modele hata döndür ki uydurmasın.
            _ => Json(new { hata = $"Bilinmeyen araç: {toolName}" })
        };
    }

    private async Task<string> SiparislerimiGetirAsync(int userId)
    {
        var now = DateTime.UtcNow;
        var orders = await _context.Orders
            .Include(o => o.OrderItems)
                .ThenInclude(oi => oi.Product)
            .Where(o => o.UserId == userId)
            .OrderByDescending(o => o.CreatedAt)
            .Take(20)   // Sohbet için son 20 sipariş yeterli
            .ToListAsync();

        if (orders.Count == 0)
        {
            return Json(new { mesaj = "Müşterinin hiç siparişi yok." });
        }

        var sonuc = orders.Select(o =>
        {
            // Reddedilen kalemler ödemeye girmez (kısmi onay). Bekleyen siparişte hepsi sayılır.
            decimal araToplam = o.OrderItems
                .Where(i => i.Status != OrderStatus.Rejected)
                .Sum(i => i.UnitPrice * i.Quantity);
            decimal indirim = o.CalculateDiscount(araToplam);

            return new
            {
                siparisNo = o.Id,
                durum = DurumTr(o.Status),
                tarih = DateTime.SpecifyKind(o.CreatedAt, DateTimeKind.Utc),
                iptalEdilebilir = OrderService.CanCancel(o, now),
                iptalSonTarih = OrderService.CanCancel(o, now)
                    ? DateTime.SpecifyKind(o.CreatedAt + OrderService.CancelWindow, DateTimeKind.Utc)
                    : (DateTime?)null,
                kuponKodu = o.CouponCode,
                araToplam,
                indirim,
                odenecek = araToplam - indirim,
                teslimat = new { o.City, o.District },
                kalemler = o.OrderItems.Select(i => new
                {
                    urun = i.Product.Name,
                    adet = i.Quantity,
                    birimFiyat = i.UnitPrice,
                    durum = DurumTr(i.Status)
                })
            };
        });

        return Json(sonuc);
    }

    private async Task<string> KuponlarimiGetirAsync(int userId)
    {
        var now = DateTime.UtcNow;

        // CouponsController'daki "my" mantığının sohbet için sadeleştirilmiş kopyası:
        // aktif, tarih aralığı tutan ve (herkese açık ya da bu kullanıcıya atanmış) kuponlar.
        var adaylar = await _context.Coupons
            .Where(c => c.IsActive
                && (!c.AssignedUsers.Any() || c.AssignedUsers.Any(a => a.UserId == userId))
                && (c.StartsAt == null || c.StartsAt <= now)
                && (c.EndsAt == null || c.EndsAt >= now))
            .Select(c => new
            {
                c.Code,
                c.Type,
                c.Value,
                c.MinOrderTotal,
                c.MaxUses,
                c.PerUserLimit,
                c.EndsAt,
                KisiyeOzel = c.AssignedUsers.Any(),
                ToplamKullanim = _context.Orders.Count(o => o.CouponId == c.Id
                    && o.Status != OrderStatus.Cancelled && o.Status != OrderStatus.Rejected),
                KendiKullanimim = _context.Orders.Count(o => o.CouponId == c.Id
                    && o.UserId == userId
                    && o.Status != OrderStatus.Cancelled && o.Status != OrderStatus.Rejected)
            })
            .ToListAsync();

        // Limiti dolmuş kuponları ele: müşteriye "kullanabilirsin" deyip sonra hata almasın.
        var kullanilabilir = adaylar
            .Where(c => (c.MaxUses == null || c.ToplamKullanim < c.MaxUses)
                && (c.PerUserLimit == null || c.KendiKullanimim < c.PerUserLimit))
            .Select(c => new
            {
                kod = c.Code,
                indirim = CouponService.Ozet(c.Type, c.Value),
                altLimit = c.MinOrderTotal,
                sonKullanma = c.EndsAt.HasValue
                    ? DateTime.SpecifyKind(c.EndsAt.Value, DateTimeKind.Utc)
                    : (DateTime?)null,
                kisiyeOzel = c.KisiyeOzel,
                kalanHakkim = c.PerUserLimit == null ? (int?)null : c.PerUserLimit - c.KendiKullanimim
            })
            .ToList();

        if (kullanilabilir.Count == 0)
        {
            return Json(new { mesaj = "Müşterinin şu an kullanabileceği bir kupon yok." });
        }

        return Json(kullanilabilir);
    }

    private async Task<string> SepetimiGetirAsync(int userId)
    {
        var now = DateTime.UtcNow;
        var items = await _context.CartItems
            .Include(c => c.Product)
            .Where(c => c.UserId == userId)
            .ToListAsync();

        if (items.Count == 0)
        {
            return Json(new { mesaj = "Sepet boş." });
        }

        var satirlar = items.Select(i =>
        {
            decimal birim = i.Product.EffectivePrice(now);
            return new
            {
                urun = i.Product.Name,
                adet = i.Quantity,
                birimFiyat = birim,
                indirimli = i.Product.IsDiscountActive(now),
                satirToplam = birim * i.Quantity
            };
        }).ToList();

        return Json(new
        {
            araToplam = satirlar.Sum(s => s.satirToplam),
            urunSayisi = satirlar.Sum(s => s.adet),
            urunler = satirlar
        });
    }

    private async Task<string> UrunAraAsync(string arama)
    {
        arama = (arama ?? "").Trim();
        if (arama.Length == 0)
        {
            return Json(new { hata = "Arama terimi boş olamaz." });
        }

        var now = DateTime.UtcNow;
        var urunler = await _context.Products
            .Where(p => p.IsActive && p.Name.Contains(arama))
            .OrderBy(p => p.Name)
            .Take(10)   // Sohbet cevabını şişirmemek için ilk 10 eşleşme
            .ToListAsync();

        if (urunler.Count == 0)
        {
            return Json(new { mesaj = $"'{arama}' için satışta ürün bulunamadı." });
        }

        var sonuc = new List<object>();
        foreach (var p in urunler)
        {
            // Stok SAYISINI sızdırmıyoruz (müşteriye kapalı bilgi); yalnızca var/yok.
            // Başka sepetlerde ayrılmış ve onay bekleyen adetler düşülmüş gerçek müsaitlik.
            int musait = await _stockService.AvailableForUserAsync(p.Id, userId: 0);
            bool indirimli = p.IsDiscountActive(now);
            sonuc.Add(new
            {
                urun = p.Name,
                fiyat = p.Price,
                indirimliFiyat = indirimli ? p.EffectivePrice(now) : (decimal?)null,
                stokDurumu = musait > 0 ? "var" : "yok"
            });
        }

        return Json(sonuc);
    }

    // Mağazadaki kategorileri (ana + alt) ve her birindeki satıştaki ürün sayısını döndürür.
    private async Task<string> KategorileriGetirAsync()
    {
        // Aktif ürünlerin kategori bazında sayımı (tek sorgu; her kategori için ayrı sorgu atmamak için).
        var sayimlar = await _context.Products
            .Where(p => p.IsActive)
            .GroupBy(p => p.CategoryId)
            .Select(g => new { KategoriId = g.Key, Adet = g.Count() })
            .ToListAsync();
        var sayimHarita = sayimlar.ToDictionary(x => x.KategoriId, x => x.Adet);

        var kategoriler = await _context.Categories
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .ToListAsync();

        // Ana kategoriler + altlarını iç içe ver. Ürün sayısı: kategorinin kendi ürünleri +
        // (ana kategoride) alt kategorilerinin ürünleri.
        var anaKategoriler = kategoriler.Where(c => c.ParentId == null).ToList();

        var sonuc = anaKategoriler.Select(ana =>
        {
            var altlar = kategoriler.Where(c => c.ParentId == ana.Id).ToList();
            int kendiAdet = sayimHarita.GetValueOrDefault(ana.Id);
            int altToplam = altlar.Sum(a => sayimHarita.GetValueOrDefault(a.Id));
            return new
            {
                kategori = ana.Name,
                urunSayisi = kendiAdet + altToplam,
                altKategoriler = altlar.Select(a => new
                {
                    ad = a.Name,
                    urunSayisi = sayimHarita.GetValueOrDefault(a.Id)
                })
            };
        });

        return Json(sonuc);
    }

    // Ürünleri kategoriye/fiyata/indirime göre süzüp sıralı biçimde (ilk 10) döndürür.
    // İsimle arama BURADA yapılmaz (urunAra onu karşılar); bu araç "keşif" sorularınadır.
    private async Task<string> UrunleriListeleAsync(
        string? kategori, decimal? enAzFiyat, decimal? enCokFiyat, bool? sadeceIndirimli, string? sirala)
    {
        var now = DateTime.UtcNow;
        var query = _context.Products.Where(p => p.IsActive);

        // Kategori süzgeci: ad eşleşen kategori(ler) + onların alt kategorileri kapsanır.
        if (!string.IsNullOrWhiteSpace(kategori))
        {
            var ad = kategori.Trim();
            var eslesenIds = await _context.Categories
                .Where(c => c.Name.Contains(ad))
                .Select(c => c.Id)
                .ToListAsync();

            if (eslesenIds.Count == 0)
            {
                return Json(new { mesaj = $"'{kategori}' adında bir kategori bulunamadı." });
            }

            // Eşleşen kategorilerin alt kategorilerini de dahil et.
            var altIds = await _context.Categories
                .Where(c => c.ParentId != null && eslesenIds.Contains(c.ParentId!.Value))
                .Select(c => c.Id)
                .ToListAsync();

            var kapsam = eslesenIds.Concat(altIds).Distinct().ToList();
            query = query.Where(p => kapsam.Contains(p.CategoryId));
        }

        // İndirim yalnızca aktif ürünler. (İfade, ProductsController'daki fiyat mantığının aynısı.)
        if (sadeceIndirimli == true)
        {
            query = query.Where(p => p.DiscountPrice != null && p.DiscountPrice < p.Price
                && (p.DiscountStart == null || p.DiscountStart <= now)
                && (p.DiscountEnd == null || p.DiscountEnd >= now));
        }
        // Fiyat aralığı GÜNCEL (indirim aktifse indirimli) fiyata göre süzülür. İfade her yerde
        // satır içi tekrarlanır çünkü projede Expression birleştirme (LINQKit) yok.
        if (enAzFiyat is decimal min)
        {
            query = query.Where(p => (p.DiscountPrice != null && p.DiscountPrice < p.Price
                && (p.DiscountStart == null || p.DiscountStart <= now)
                && (p.DiscountEnd == null || p.DiscountEnd >= now)
                    ? p.DiscountPrice!.Value : p.Price) >= min);
        }
        if (enCokFiyat is decimal max)
        {
            query = query.Where(p => (p.DiscountPrice != null && p.DiscountPrice < p.Price
                && (p.DiscountStart == null || p.DiscountStart <= now)
                && (p.DiscountEnd == null || p.DiscountEnd >= now)
                    ? p.DiscountPrice!.Value : p.Price) <= max);
        }

        // "Yeni gelenler": son 7 günde eklenenlerle sınırla (vitrindeki hızlı butonla aynı anlam).
        if (sirala == "yeniGelenler")
        {
            query = query.Where(p => p.CreatedAt >= now.AddDays(-7));
        }

        query = sirala switch
        {
            "fiyatArtan" => query.OrderBy(p => p.DiscountPrice != null && p.DiscountPrice < p.Price
                && (p.DiscountStart == null || p.DiscountStart <= now)
                && (p.DiscountEnd == null || p.DiscountEnd >= now)
                    ? p.DiscountPrice!.Value : p.Price),
            "fiyatAzalan" => query.OrderByDescending(p => p.DiscountPrice != null && p.DiscountPrice < p.Price
                && (p.DiscountStart == null || p.DiscountStart <= now)
                && (p.DiscountEnd == null || p.DiscountEnd >= now)
                    ? p.DiscountPrice!.Value : p.Price),
            "enCokBegenilen" => query.OrderByDescending(p => p.Favorites.Count()),
            "enCokSatan" => query.OrderByDescending(p => _context.OrderItems
                .Where(oi => oi.ProductId == p.Id
                    && oi.Status == OrderStatus.Approved
                    && oi.Order.Status != OrderStatus.Cancelled
                    && oi.Order.Status != OrderStatus.Rejected)
                .Sum(oi => (int?)oi.Quantity) ?? 0),
            "yeniGelenler" => query.OrderByDescending(p => p.CreatedAt),
            // Varsayılan "öne çıkanlar": favori + puan (vitrindeki "önerilen" mantığına yakın).
            _ => query
                .OrderByDescending(p => p.Favorites.Count())
                .ThenByDescending(p => p.Reviews.Any() ? p.Reviews.Average(r => r.Rating) : 0)
        };

        // Puan ve indirimli fiyat SQL'de hesaplanır (materialize sonrası Reviews yüklü olmazdı).
        var satirlar = await query
            .Take(10)
            .Select(p => new
            {
                p.Id,
                Urun = p.Name,
                Kategori = p.Category.Name,
                Fiyat = p.Price,
                Indirimli = p.DiscountPrice != null && p.DiscountPrice < p.Price
                    && (p.DiscountStart == null || p.DiscountStart <= now)
                    && (p.DiscountEnd == null || p.DiscountEnd >= now),
                IndirimliFiyat = p.DiscountPrice,
                Puan = p.Reviews.Any() ? Math.Round(p.Reviews.Average(r => r.Rating), 1) : 0
            })
            .ToListAsync();

        if (satirlar.Count == 0)
        {
            return Json(new { mesaj = "Bu ölçütlere uyan satışta ürün bulunamadı." });
        }

        var sonuc = new List<object>();
        foreach (var s in satirlar)
        {
            // Stok SAYISINI sızdırmıyoruz; yalnızca var/yok (urunAra ile aynı gizlilik).
            int musait = await _stockService.AvailableForUserAsync(s.Id, userId: 0);
            sonuc.Add(new
            {
                urun = s.Urun,
                kategori = s.Kategori,
                fiyat = s.Fiyat,
                indirimliFiyat = s.Indirimli ? s.IndirimliFiyat : (decimal?)null,
                stokDurumu = musait > 0 ? "var" : "yok",
                puan = s.Puan > 0 ? (double?)s.Puan : null
            });
        }

        return Json(sonuc);
    }

    // ===== YAZMA araçları =====
    // Not: Bu araçların hepsi userId'yi DIŞARIDAN alır (JWT). Backend kuralları (stok, sahiplik,
    // iptal süresi) mevcut servis/kontrol mantığıyla aynıdır — burada ikinci bir kural seti yok.

    // Ürün adını tekil bir ürüne çözer. Belirsizlik varsa (0 veya birden çok eşleşme) sepet
    // işlemi yapılmaz; çağırana durum bildirilir ki model müşteriye sorabilsin.
    private async Task<(Product? urun, string? hata)> UrunuCozAsync(string urunAdi)
    {
        urunAdi = (urunAdi ?? "").Trim();
        if (urunAdi.Length == 0) return (null, "Ürün adı boş olamaz.");

        var eslesmeler = await _context.Products
            .Where(p => p.IsActive && p.Name.Contains(urunAdi))
            .Take(6)
            .ToListAsync();

        if (eslesmeler.Count == 0)
            return (null, $"'{urunAdi}' adında satışta bir ürün bulunamadı.");

        // Birden çok eşleşmede birebir (büyük/küçük harf duyarsız) ad tekse onu seç.
        var birebir = eslesmeler
            .Where(p => string.Equals(p.Name, urunAdi, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (birebir.Count == 1) return (birebir[0], null);

        if (eslesmeler.Count > 1)
        {
            var adlar = string.Join(", ", eslesmeler.Select(p => p.Name));
            return (null, $"Birden fazla ürün eşleşti ({adlar}). Hangisini kastettiğini müşteriye sor.");
        }

        return (eslesmeler[0], null);
    }

    private async Task<string> SepeteEkleAsync(int userId, string urunAdi, int adet)
    {
        if (adet < 1) adet = 1;

        var (urun, hata) = await UrunuCozAsync(urunAdi);
        if (urun == null) return Json(new { hata });

        var mevcut = await _context.CartItems
            .FirstOrDefaultAsync(c => c.UserId == userId && c.ProductId == urun.Id);

        int yeniAdet = (mevcut?.Quantity ?? 0) + adet;

        // Stok kontrolü CartController.AddToCart ile aynı: müsait adede bakar (başkalarının
        // rezervasyonları ve onay bekleyen siparişler düşülmüş).
        int musait = await _stockService.AvailableForUserAsync(urun.Id, userId);
        if (yeniAdet > musait)
        {
            return Json(new
            {
                hata = musait == 0
                    ? $"{urun.Name} şu anda müsait değil (stoktaki adetler başka sepetlerde ayrılmış olabilir)."
                    : $"{urun.Name} ürününden en fazla {musait} adet alınabilir."
            });
        }

        if (mevcut != null)
        {
            mevcut.Quantity = yeniAdet;
            mevcut.AddedAt = DateTime.UtcNow;   // Adet artınca rezervasyon süresi yenilenir
        }
        else
        {
            _context.CartItems.Add(new CartItem
            {
                UserId = userId,
                ProductId = urun.Id,
                Quantity = adet,
                AddedAt = DateTime.UtcNow
            });
        }

        await _context.SaveChangesAsync();
        return Json(new
        {
            mesaj = $"{urun.Name} sepete eklendi.",
            urun = urun.Name,
            eklenenAdet = adet,
            sepettekiToplamAdet = yeniAdet
        });
    }

    private async Task<string> SepettenCikarAsync(int userId, string urunAdi)
    {
        urunAdi = (urunAdi ?? "").Trim();
        if (urunAdi.Length == 0) return Json(new { hata = "Ürün adı boş olamaz." });

        var satirlar = await _context.CartItems
            .Include(c => c.Product)
            .Where(c => c.UserId == userId && c.Product.Name.Contains(urunAdi))
            .ToListAsync();

        if (satirlar.Count == 0)
            return Json(new { hata = $"Sepette '{urunAdi}' ile eşleşen ürün yok." });

        if (satirlar.Count > 1)
        {
            var adlar = string.Join(", ", satirlar.Select(s => s.Product.Name));
            return Json(new { hata = $"Sepette birden fazla ürün eşleşti ({adlar}). Hangisini kastettiğini müşteriye sor." });
        }

        var ad = satirlar[0].Product.Name;
        _context.CartItems.Remove(satirlar[0]);   // Satır silinince rezervasyon da serbest kalır
        await _context.SaveChangesAsync();
        return Json(new { mesaj = $"{ad} sepetten çıkarıldı." });
    }

    private async Task<string> SiparisVerAsync(int userId, string? kuponKodu)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null) return Json(new { hata = "Kullanıcı bulunamadı." });

        // Teslimat bilgisi KAYITLI profilden alınır — bot adres uydurmaz, müşteri sohbette
        // adres yazmak zorunda kalmaz. Eksikse sipariş oluşturulmaz, müşteri yönlendirilir.
        if (string.IsNullOrWhiteSpace(user.City) || string.IsNullOrWhiteSpace(user.District)
            || string.IsNullOrWhiteSpace(user.Address))
        {
            return Json(new { hata = "Teslimat adresiniz kayıtlı değil. Lütfen Hesabım sayfasından il, ilçe ve açık adresinizi ekleyip tekrar deneyin." });
        }
        if (string.IsNullOrWhiteSpace(user.Phone))
        {
            return Json(new { hata = "Telefon numaranız kayıtlı değil. Lütfen Hesabım sayfasından ekleyip tekrar deneyin." });
        }

        var dto = new CheckoutDto
        {
            RecipientName = string.IsNullOrWhiteSpace(user.FullName) ? user.Username : user.FullName!,
            Phone = user.Phone!,
            City = user.City!,
            District = user.District!,
            Address = user.Address!,
            CouponCode = string.IsNullOrWhiteSpace(kuponKodu) ? null : kuponKodu
        };

        try
        {
            // Tüm iş kuralları (boş sepet, stok yetersizliği, kupon geçerliliği) OrderService'te.
            var order = await _orderService.CreateOrderFromCartAsync(userId, dto);

            decimal araToplam = order.OrderItems.Sum(i => i.UnitPrice * i.Quantity);
            decimal indirim = order.CalculateDiscount(araToplam);

            return Json(new
            {
                mesaj = "Siparişiniz alındı ve onay için mağazaya iletildi.",
                siparisNo = order.Id,
                durum = DurumTr(order.Status),
                araToplam,
                indirim,
                odenecek = araToplam - indirim,
                teslimat = $"{dto.City} / {dto.District}"
            });
        }
        catch (Exception ex)
        {
            // "Sepetiniz boş", stok/kupon hataları buraya düşer; mesaj müşteriye aktarılabilir.
            return Json(new { hata = ex.Message });
        }
    }

    private async Task<string> SiparisIptalEtAsync(int userId, int siparisNo)
    {
        if (siparisNo <= 0) return Json(new { hata = "Geçerli bir sipariş numarası gerekli." });

        try
        {
            // Sahiplik (o.UserId == userId), 'Bekliyor' durumu ve 1 saatlik süre kuralını
            // OrderService.CancelOrderAsync zorlar. Başka birinin siparişi "bulunamadı" döner.
            await _orderService.CancelOrderAsync(siparisNo, userId);
            return Json(new { mesaj = $"{siparisNo} numaralı sipariş iptal edildi." });
        }
        catch (Exception ex)
        {
            return Json(new { hata = ex.Message });
        }
    }

    // Gemini sayıları JSON'da bazen ondalık (12.0) olarak yollar; int'e güvenli çevirir.
    private static int? IntAl(JsonNode? node)
    {
        if (node == null) return null;
        try { return node.GetValue<int>(); }
        catch
        {
            try { return (int)Math.Round(node.GetValue<double>()); }
            catch { return null; }
        }
    }

    // Gemini sayıları int/double/string olarak yollayabilir; decimal'e güvenli çevirir.
    private static decimal? DecimalAl(JsonNode? node)
    {
        if (node == null) return null;
        try { return node.GetValue<decimal>(); }
        catch
        {
            try { return (decimal)node.GetValue<double>(); }
            catch
            {
                try { return decimal.Parse(node.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture); }
                catch { return null; }
            }
        }
    }

    private static bool? BoolAl(JsonNode? node)
    {
        if (node == null) return null;
        try { return node.GetValue<bool>(); }
        catch
        {
            try { return bool.Parse(node.GetValue<string>()); }
            catch { return null; }
        }
    }

    private static string DurumTr(OrderStatus status) => status switch
    {
        OrderStatus.Pending => "Bekliyor",
        OrderStatus.Approved => "Onaylandı",
        OrderStatus.Rejected => "Reddedildi",
        OrderStatus.Shipped => "Kargoya verildi",
        OrderStatus.Delivered => "Teslim edildi",
        OrderStatus.Cancelled => "İptal edildi",
        _ => status.ToString()
    };

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        // Türkçe karakterler tool_result içinde okunur kalsın (\u kaçışı olmasın).
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static string Json(object value) => JsonSerializer.Serialize(value, JsonOpts);
}
