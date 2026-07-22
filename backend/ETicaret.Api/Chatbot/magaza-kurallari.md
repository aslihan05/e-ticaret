# Mağaza Kuralları (Chatbot Bilgi Metni)

> Bu metin chatbot'un sistem talimatına gömülecek. Bot yalnızca burada yazanları
> söyleyebilir; burada olmayan bir kuralı (özellikle süre, ücret, iade) **uydurmamalı**,
> "bu konuda müşteri hizmetlerine yönlendiriyorum" demeli.
>
> İşaretlemeler:
> - ✅ **Koddan doğrulandı** — sitenin gerçek davranışı budur, güvenle söylenebilir.
> - 📝 **Varsayılan politika** — kodda karşılığı yok; Türkiye e-ticaret normlarına göre
>   makul bir varsayılan yazıldı. Aslıhan istediğinde değiştirebilir; bot bu haliyle cevap verir.

---

## 1. Sipariş durumları ✅

Bir siparişin geçebileceği durumlar ve anlamları:

- **Bekliyor** — Sipariş alındı, mağaza onayı bekliyor. Her sipariş bu durumda başlar.
- **Onaylandı** — Mağaza siparişi onayladı, hazırlanıyor. Stok bu aşamada düşülür.
- **Kargoya verildi** — Sipariş yola çıktı.
- **Teslim edildi** — Sipariş müşteriye ulaştı.
- **Reddedildi** — Mağaza siparişi (veya bazı kalemlerini) onaylamadı.
- **İptal edildi** — Müşteri kendisi iptal etti.

Durumlar yalnızca şu sırayla ilerler: Bekliyor → Onaylandı → Kargoya verildi → Teslim edildi.
Geriye dönüş yoktur.

**Kısmi onay:** Bir sipariş birden çok ürün içeriyorsa, mağaza bazı kalemleri onaylayıp
bazılarını reddedebilir. Bu durumda müşteri yalnızca onaylanan kalemler için ödeme yapar;
reddedilen kalemler toplamdan ve kupon indirimi hesabından düşülür.

## 2. Sipariş iptali ✅

- Müşteri siparişini **yalnızca "Bekliyor" durumundayken** iptal edebilir.
- İptal, sipariş verildikten sonra **yalnızca 1 saat içinde** yapılabilir. Süre dolunca
  iptal butonu çalışmaz.
- Onaylanmış, kargoya verilmiş veya teslim edilmiş sipariş müşteri tarafından iptal edilemez.

**İptal süresi geçtiyse ne olur?** 📝
- 1 saatlik süre dolduysa ama sipariş henüz kargoya verilmediyse (Bekliyor/Onaylandı),
  müşteri vazgeçmek isterse müşteri hizmetlerine yazabilir; sipariş kargolanmadan
  durdurulabilir.
- Sipariş kargoya verildiyse artık iptal edilemez; müşteri teslimatı bekleyip aşağıdaki
  **iade** hakkını kullanabilir.

## 3. İade 📝

> 📝 **Varsayılan politika** (kodda otomatik akışı yok; talep müşteri hizmetleri üzerinden
> elle yürütülür). Değerler Türkiye'deki mesafeli satış normlarına göre yazıldı.

- Müşteri, teslim aldığı üründen memnun kalmazsa **teslimattan sonra 14 gün içinde**
  cayma hakkını kullanıp iade edebilir.
- Ürün **kullanılmamış, denenmemiş ve orijinal ambalajı bozulmamış** olmalıdır. Hijyen
  gerektiren ürünlerde (iç giyim, kozmetik vb.) ambalaj açıldıysa iade kabul edilmez.
- İade talebi müşteri hizmetlerine iletilir; onaylanınca müşteriye iade kargo yöntemi bildirilir.
- Ürün mağazaya ulaşıp incelendikten sonra **para iadesi**, ödemenin yapıldığı yönteme
  **3–10 iş günü** içinde yapılır.
- Ürün ayıplı/hatalı ise iade kargo ücreti mağazaya aittir; müşterinin cayma hakkı
  kaynaklı iadelerde kargo ücreti müşteriye ait olabilir.

## 4. Kargo 📝

> 📝 **Varsayılan politika** — kodda süre/ücret tanımlı değil; makul varsayılanlar yazıldı.

- Onaylanan siparişler genellikle **2–4 iş günü** içinde teslim edilir.
- Kargo ücreti **250 TL ve üzeri siparişlerde ücretsizdir**; altındaki siparişlerde
  **49,90 TL** kargo ücreti uygulanır.
- Sipariş "Kargoya verildi" durumuna geçtiğinde müşteriye e-posta ile bilgi verilir.
  Kargo takip numarası, çalışılan anlaşmalı kargo firması tarafından müşterinin
  telefonuna/e-postasına iletilir.

## 5. Teslimat bilgileri ✅

Sipariş verirken **ad-soyad, telefon, il, ilçe ve açık adres zorunludur.** Bunlar
eksikse sipariş oluşturulamaz. Müşterinin ilk siparişindeki adres, sonraki siparişlerde
hazır gelmesi için hesabına kaydedilir.

## 6. Kuponlar ✅

- Kupon iki türlü olabilir: **yüzde indirim** (örn. %10) veya **tutar indirimi** (örn. 50 TL).
- Bir kuponun **alt sepet limiti** olabilir (örn. "150 TL üstü sepette geçerli").
- Kupon **kişiye özel** olabilir (yalnızca belirli müşteriler kullanır) veya herkese açık olabilir.
- Kuponun **toplam kullanım limiti** ve **kişi başına kullanım limiti** olabilir.
- Kuponun **başlangıç/bitiş tarihi** olabilir; bu aralık dışında geçersizdir.
- İndirim hiçbir zaman sepet tutarından büyük olamaz.
- İptal edilen veya reddedilen siparişler kupon kullanım hakkını **geri kazandırır**
  (bu siparişler kullanım sayısına dahil edilmez).
- Müşteri "Kuponlarım" bölümünden şu an kullanabileceği kuponları görebilir.

## 7. Ürün fiyatı ve indirim ✅

- Bir ürünün **indirimli fiyatı** olabilir; indirim yalnızca belirlenen tarih aralığında
  geçerlidir (tarih verilmezse süresizdir).
- Sipariş verildiğinde fiyat o anki geçerli fiyattan (indirim aktifse indirimli fiyattan)
  dondurulur; sonradan fiyat değişse de siparişin tutarı değişmez.

## 8. Botun sınırları (davranış kuralı)

- Bot yalnızca **giriş yapmış müşterinin kendi** siparişlerini, kuponlarını ve sepetini görebilir.
  Başka bir müşterinin verisini asla getiremez, müşteri numarası girse bile.
- Bot fiyat/indirim/stok gibi bilgileri **canlı sistemden** okur, tahmin etmez.
- Bot yalnızca bu metinde yazan kuralları söyler. İade/kargo gibi konularda buradaki
  politikayı aktarır; burada cevabı olmayan özel bir durumda (örn. hasarlı ürün fotoğrafı,
  fatura değişikliği) müşteri hizmetlerine yönlendirir, kural uydurmaz.
