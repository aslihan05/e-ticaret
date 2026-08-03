# E-Ticaret

Öğrenme amaçlı, uçtan uca çalışan full-stack bir e-ticaret uygulaması. ASP.NET Core Web API backend'i ve framework'süz (düz HTML/CSS/JavaScript) bir frontend'den oluşur.

## Özellikler

- **Kullanıcı sistemi**: Kayıt ve giriş sayfaları, JWT tabanlı kimlik doğrulama, rol bazlı yetkilendirme (Admin / Customer)
- **Vitrin**: Ürün listeleme, canlı arama, kategoriye göre filtreleme, fiyata göre sıralama, ürün detay sayfası, **süreli indirim** (indirimli fiyat + başlangıç/bitiş tarihi)
- **Sunucu tarafı sayfalama ve filtreleme**: Arama, kategori, fiyat aralığı, **puan (en az X yıldız)**, **sadece indirimli**, **sadece stokta olanlar**, sıralama (fiyat ↑↓, en çok beğenilen) ve sayfalama `GET /products/browse` ile veritabanında yapılır — tarayıcı yalnızca görüntülenen sayfayı indirir. Fiyat filtresi/sıralaması **geçerli fiyat** (indirim aktifse indirimli) üzerinden çalışır; arama kutusu 350 ms debounce'ludur
- **Header açılır menüsü**: **Kategoriler & Filtreler** artık logonun yanındaki tetikleyiciden **çapraz açılan** bir kutuda toplanır; içinde kategori listesi ve tüm filtreler bulunur (kenar çubuğu kaldırıldı, vitrin tek kolon)
- **Yuvarlak hızlı butonlar**: Logonun altında canlı degrade rozetli üç büyük buton — **En Çok Satanlar** (onaylı satış adedine göre), **En Çok Favori Seçilenler** (favori sayısına göre) ve **Senin İçin Önerilenler** (kullanıcının favori/sipariş geçmişindeki kategoriler önce, sonra popülerlik/puan). `GET /products/browse?sort=best-selling|most-favorited|recommended` ile veritabanında sıralanır
- **Yorum ve puanlama**: 1-5 yıldız + isteğe bağlı yorum; **yalnızca ürünü onaylanmış siparişte satın almış kullanıcı** yorum yazabilir, kullanıcı başına ürün başına tek yorum. Ortalama puan ve yıldız dağılımı ürün detayında, yıldızlar vitrin kartlarında görünür. Kullanıcı kendi yorumunu düzenler/siler; admin moderasyon için siler (log'a düşer). Yorumlarda kullanıcı adları maskelenir (`aslihan` → `a*****n`).
- **Favoriler**: Vitrin kartlarında ve ürün detayında kalp butonu, ayrı **Favorilerim** sayfası. Ekleme/çıkarma idempotenttir (aynı istek iki kez gelirse hata değil, aynı sonuç); satıştan kaldırılan ürünler listede görünmez
- **Kupon / indirim kodu**: Yüzde veya tutar indirimi, alt sepet limiti, geçerlilik tarihi, toplam ve kullanıcı başına kullanım limiti. Sepette anında önizleme, admin panelinden yönetim. Kod büyük/küçük harf duyarsızdır (`yaz10` = `YAZ10`)
- **Sepet**: Sepete ekleme (JWT korumalı), adet güncelleme, satır silme, sepet rozeti
- **Sipariş akışı**: Sepetten sipariş oluşturma (checkout), sipariş anındaki fiyatın saklanması; siparişler admin onayına düşer, **onayda stok otomatik düşer**; kullanıcı **sipariş geçmişi** sayfasından durumunu takip eder. Sipariş sonrası **"siparişin alındı" ekranında** satın alınan ürünlerin görseli, adı, adedi ve tutarıyla birlikte sipariş özeti gösterilir
- **Hediye kutusu daveti**: Menüdeki 🎁 Hediye Kutusu bağlantısı, müşteri açmayı unutmasın diye arada bir hafifçe sallanır ve yumuşak bir parıltıyla nefes alır (`prefers-reduced-motion` tercihine saygılı)
- **Yapay zekâ destekli müşteri asistanı (chatbot)**: Sağ alttaki destek butonundan açılan sohbet kutusu. Hazır cevap veren bir SSS botu değil, **tool use** ile çalışır: soruyu cevaplamak için sunucudaki araçları çağırıp gerçek veritabanı verisiyle konuşur.
  - **Okuma araçları**: kendi siparişleri, kuponları, sepeti, **favori listesi**, ürün arama, kategori listesi, filtreli/sıralı ürün listesi
  - **Yazma araçları**: sepete ekleme/çıkarma, **favorilere ekleme/çıkarma**, sipariş oluşturma, sipariş iptali. Sepet ve favori işlemleri geri alınabilir olduğu için onaysız yapılır; **sipariş verme ve iptal için müşteriden açık onay istenir**
  - **Güvenlik**: Araçların hiçbiri kullanıcı kimliğini parametre olarak almaz — kimlik her zaman JWT'den gelir, yani müşteri sohbete id yazarak başkasının verisine ulaşamaz (IDOR yok). Stok **adedi**, maliyet/kâr gibi kapalı bilgiler hiçbir araçtan dönmez; iş kuralları mevcut servislerden (`OrderService`, `StockService`) yeniden kullanılır, bot için ikinci bir kural seti yoktur
- **Admin paneli**: Sipariş onaylama/reddetme, kullanıcı yönetimi, istek logları, satış istatistikleri — hepsi `admin.html` arayüzünden yönetilir
- **Loglama**: Değişiklik yapan tüm isteklerin middleware ile otomatik kaydı. Admin log tablosu dışarıdan bakan biri için okunur tasarlanmıştır: her kaydın **Açıklama** sütununda kim/neyi/hangi sonuçla yaptığı düz Türkçe yazar; işlevsiz **Seviye** sütunu kaldırılmış, önem bilgisi renkli **Durum** (HTTP kodu) rozetiyle verilir

## Teknoloji yığını

| Katman | Seçim |
|---|---|
| Backend | .NET 8 / ASP.NET Core Web API (C#) |
| Veritabanı | SQL Server |
| ORM | Entity Framework Core 8 (Code-First) |
| Kimlik doğrulama | JWT (JSON Web Token) |
| Frontend | Düz HTML / CSS / JavaScript (framework yok) |

## Proje yapısı

```
E-Ticaret/
├── backend/
│   └── ETicaret.Api/
│       ├── Controllers/            (Auth, Products, Categories, Cart, Orders, Reviews, Favorites, Profile, Admin)
│       ├── Models/
│       │   ├── Entities/           (Role, User, Category, Product, ProductImage, CartItem,
│       │   │                        Order, OrderItem, Review, Favorite, Log)
│       │   └── Dtos/
│       ├── Data/AppDbContext.cs
│       ├── Migrations/
│       ├── Services/               (AuthService, OrderService, StockService, EmailService)
│       ├── Middleware/             (LoggingMiddleware)
│       ├── Program.cs
│       └── appsettings.json
└── frontend/
    ├── index.html                  (tanıtım / landing sayfası)
    ├── shop.html                   (vitrin: listeleme, arama, filtre, sıralama)
    ├── product.html                (ürün detayı + yorumlar)
    ├── cart.html                   (sepet + checkout)
    ├── favorites.html              (favori ürünler listesi)
    ├── orders.html                 (kullanıcı sipariş geçmişi + durum takibi)
    ├── profil.html                 (hesap bilgileri)
    ├── admin.html                  (admin paneli: sipariş/ürün/kullanıcı yönetimi)
    ├── login.html / register.html  (giriş / kayıt)
    ├── css/                        (style.css, login.css)
    ├── js/
    │   ├── api.js                  (API taban adresi, fetch yardımcıları, JWT başlığı)
    │   ├── site.js                 (sayfalar arası ortak: nav, sepet rozeti, sepete ekleme, favoriler)
    │   ├── admin.js / orders.js    (admin paneli / sipariş geçmişi mantığı)
    │   ├── favorites.js / profil.js / landing.js
    │   └── home.js / product.js / cart.js / auth.js / register.js
    └── images/
```

## Veritabanı şeması

- **Roles**: `Id`, `Name` (Admin / Customer)
- **Users**: `Id`, `Username`, `PasswordHash`, `CreatedAt`, `CreatedBy`, `RoleId` (FK → Roles)
- **Categories**: `Id`, `Name`
- **Products**: `Id`, `Name`, `Description`, `Price`, `Stock`, `CategoryId` (FK), `ImageUrl`, `IsActive`, `DiscountPrice`, `DiscountStart`, `DiscountEnd` (süreli indirim; tarih aralığı dışında normal fiyat geçerli)
- **CartItems**: `Id`, `UserId` (FK → Users), `ProductId` (FK → Products), `Quantity`
- **Orders**: `Id`, `UserId` (FK → Users), `Status` (Pending / Approved / Rejected), `CreatedAt`, `ApprovedAt`, `ApprovedBy` (FK → Users)
- **OrderItems**: `Id`, `OrderId` (FK), `ProductId` (FK), `Quantity`, `UnitPrice` (sipariş anındaki fiyatın anlık görüntüsü), `Status` (kalem bazında durum: Pending / Approved / Rejected)
- **Reviews**: `Id`, `ProductId` (FK), `UserId` (FK), `Rating` (1-5), `Comment` (nullable), `CreatedAt`, `UpdatedAt` (nullable; doluysa "düzenlendi" gösterilir) — `(ProductId, UserId)` benzersiz indeks: bir kullanıcı bir ürüne tek yorum
- **Favorites**: `Id`, `UserId` (FK), `ProductId` (FK), `CreatedAt` — `(UserId, ProductId)` benzersiz indeks: aynı ürün listeye iki kez giremez
- **Coupons**: `Id`, `Code` (benzersiz, büyük harf), `Type` (Percent / Amount), `Value`, `MinOrderTotal`, `MaxUses`, `PerUserLimit`, `StartsAt`, `EndsAt`, `IsActive`, `CreatedAt` — kullanım sayacı **yoktur**, kullanım `Orders` üzerinden sayılır
- **Orders** (kupon alanları): `CouponId` (FK), `CouponCode`, `CouponType`, `CouponValue`, `CouponMinOrderTotal` — kuponun **şartları** sipariş anında kopyalanır; indirim tutarı saklanmaz
- **Logs**: `Id`, `UserId` (nullable FK → Users), `Action`, `Details`, `Timestamp`

**İş kuralı (kupon indirimi):** Kuponun *şartları* (tür, değer, alt limit) sipariş anında `Order` üzerine kopyalanır; *indirim tutarı* saklanmaz, her görüntülemede reddedilmemiş kalemlerin toplamı üzerinden yeniden hesaplanır. Sebebi kısmi onay: tutar dondurulsaydı, 1328 TL'lik sepete verilen %10'luk 132 TL indirim, pahalı kalem reddedilince kalan 78 TL'yi tamamen silerdi. Alt limit de gerçekleşen tutara göre kontrol edilir — kalan sepet limitin altına düşerse indirim uygulanmaz (aksi halde ürün bedavaya giderdi).

**İş kuralı (kupon kullanım sayısı):** Kuponda sayaç tutulmaz; kullanım, o kuponu taşıyan **iptal/red edilmemiş** siparişler sayılarak bulunur. Böylece sipariş iptal edilince kullanım hakkı kendiliğinden geri gelir ve sayaç ile gerçek arasında fark oluşamaz.

**İş kuralı (yorum hakkı):** Bir kullanıcı ancak ürünün `OrderItems` satırı `Approved` olan ve siparişi iptal/red edilmemiş bir alımı varsa yorum yazabilir. Yani "onay bekleyen" siparişle yorum yapılamaz — puanların güvenilirliği bu kuraldan gelir.

**İş kuralı:** Bir sipariş onaylandığında (`Order.Status = Approved`), ilgili `OrderItems` satırlarındaki her ürün için `Product.Stock` düşürülür. Onay öncesi (`Pending`) veya reddedilen (`Rejected`) siparişlerde stok değişmez.

## Kurulum ve çalıştırma

### Gereksinimler

- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- SQL Server (Windows Authentication ile erişim varsayılır — connection string'i ortamına göre değiştirebilirsin)

### Backend

1. **Repoyu klonla**
   ```bash
   git clone https://github.com/aslihan05/e-ticaret.git
   cd e-ticaret/backend/ETicaret.Api
   ```

2. **Veritabanını oluştur**
   ```sql
   CREATE DATABASE ETicaretDb;
   ```

3. **Gizli bilgileri User Secrets ile ayarla** (bu bilgiler `appsettings.json`'da tutulmaz, git'e girmez)
   ```bash
   dotnet user-secrets init
   dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=localhost;Database=ETicaretDb;Trusted_Connection=True;TrustServerCertificate=True;"
   dotnet user-secrets set "Jwt:Key" "en-az-32-karakter-uzunlugunda-gizli-bir-anahtar-belirle"
   ```

4. **Migration'ları uygula** (tabloları oluşturur)
   ```bash
   dotnet tool install --global dotnet-ef   # ilk kurulumda gerekiyorsa
   dotnet ef database update
   ```

5. **Başlangıç rollerini ekle**
   ```sql
   INSERT INTO Roles (Name) VALUES ('Admin'), ('Customer');
   ```

6. **Uygulamayı çalıştır**
   ```bash
   dotnet run
   ```
   API `http://localhost:5113` adresinde ayağa kalkar; `/swagger` yolundan tüm uç noktaları interaktif deneyebilirsin.

7. **İlk admin kullanıcıyı oluştur:** `POST /api/auth/register` ile bir kullanıcı kaydet (varsayılan rol `Customer`), sonra veritabanında yükselt:
   ```sql
   UPDATE Users SET RoleId = 1 WHERE Username = 'kullanici_adin';
   ```

### Frontend

8. `frontend/` klasörünü **5500 portunda** bir statik sunucuyla aç (backend'in CORS ayarı 5500 ve 5501'e izinlidir):
   ```bash
   npx http-server frontend -p 5500
   ```
   Alternatif: VS Code **Live Server** eklentisiyle `frontend/index.html`'i aç. (Not: Live Server, VS Code'da açık olan klasörü kök alır — proje kökü açıksa adres `http://127.0.0.1:5500/frontend/index.html` olur.)

## API uç noktaları (özet)

| Alan | Uç noktalar |
|---|---|
| Auth | `POST /api/auth/register`, `POST /api/auth/login` |
| Kategoriler | `GET /api/categories`, `POST /api/categories` (Admin), `DELETE /api/categories/{id}` (Admin) |
| Ürünler | `GET /api/products` (tüm katalog — admin paneli ve tanıtım sayfası kullanır; `?search=`, `?includeInactive=` Admin), `GET /api/products/{id}`, `POST/PUT/DELETE /api/products` (Admin) |
| Vitrin listesi | `GET /api/products/browse` — sayfalanmış müşteri görünümü. Parametreler: `search`, `categoryId`, `minPrice`, `maxPrice`, `sort` (`price-asc` / `price-desc` / `rating-desc`), `discountOnly`, `page`, `pageSize` (varsayılan 12, en fazla 48). Cevap: `{ items, page, pageSize, total, totalPages }` |
| Yorumlar | `GET /api/products/{id}/reviews` (herkese açık; özet + dağılım + `canReview`), `POST /api/products/{id}/reviews` (satın almış kullanıcı), `PUT /api/reviews/{id}` (kendi yorumu), `DELETE /api/reviews/{id}` (kendi yorumu veya Admin) |
| Favoriler | `GET /api/favorites` (kendi listesi), `GET /api/favorites/ids` (kalpleri işaretlemek için id listesi), `POST /api/favorites/{productId}`, `DELETE /api/favorites/{productId}` — hepsi giriş gerektirir |
| Sepet | `GET/POST /api/cart`, `PUT/DELETE /api/cart/{id}` (giriş gerektirir) |
| Kuponlar | `POST /api/coupons/apply` (sepette önizleme — tutar istemciden alınmaz, sunucu kendi sepetinden hesaplar), `GET /api/coupons` (Admin), `POST /api/coupons` (Admin), `PUT /api/coupons/{id}` (Admin), `DELETE /api/coupons/{id}` (Admin — silmez, kapatır) |
| Siparişler | `POST /api/orders` (checkout; `couponCode` opsiyonel), `GET /api/orders` (kendi geçmişi) |
| Chatbot | `POST /api/chatbot` (giriş gerektirir) — gövde: `{ messages: [{ role, content }] }`, cevap: `{ reply }`. Araç çağrıları tamamen sunucuda yürütülür, istemciye taşınmaz |
| Admin | `GET /api/admin/orders`, `PUT /api/admin/orders/{id}/approve`, `PUT /api/admin/orders/{id}/reject`, `GET/DELETE /api/admin/users`, `GET /api/admin/logs`, `GET /api/admin/analytics` |

Tüm uç noktaların tam şeması (istek/cevap gövdeleri dahil) için uygulamayı çalıştırıp `/swagger` adresine bakabilirsin.
