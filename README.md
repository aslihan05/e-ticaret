# E-Ticaret

Öğrenme amaçlı, uçtan uca çalışan full-stack bir e-ticaret uygulaması. ASP.NET Core Web API backend'i ve framework'süz (düz HTML/CSS/JavaScript) bir frontend'den oluşur.

## Özellikler

- **Kullanıcı sistemi**: Kayıt ve giriş sayfaları, JWT tabanlı kimlik doğrulama, rol bazlı yetkilendirme (Admin / Customer)
- **Vitrin**: Ürün listeleme, canlı arama, kategoriye göre filtreleme, fiyata göre sıralama, ürün detay sayfası, **süreli indirim** (indirimli fiyat + başlangıç/bitiş tarihi)
- **Sepet**: Sepete ekleme (JWT korumalı), adet güncelleme, satır silme, sepet rozeti
- **Sipariş akışı**: Sepetten sipariş oluşturma (checkout), sipariş anındaki fiyatın saklanması; siparişler admin onayına düşer, **onayda stok otomatik düşer**; kullanıcı **sipariş geçmişi** sayfasından durumunu takip eder
- **Admin paneli**: Sipariş onaylama/reddetme, kullanıcı yönetimi, istek logları, satış istatistikleri — hepsi `admin.html` arayüzünden yönetilir
- **Loglama**: Değişiklik yapan tüm isteklerin middleware ile otomatik kaydı

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
│       ├── Controllers/            (Auth, Products, Categories, Cart, Orders, Admin)
│       ├── Models/
│       │   ├── Entities/           (Role, User, Category, Product, CartItem, Order, OrderItem, Log)
│       │   └── Dtos/
│       ├── Data/AppDbContext.cs
│       ├── Migrations/
│       ├── Services/               (AuthService, OrderService)
│       ├── Middleware/             (LoggingMiddleware)
│       ├── Program.cs
│       └── appsettings.json
└── frontend/
    ├── index.html                  (vitrin: listeleme, arama, filtre, sıralama)
    ├── product.html                (ürün detayı)
    ├── cart.html                   (sepet + checkout)
    ├── orders.html                 (kullanıcı sipariş geçmişi + durum takibi)
    ├── admin.html                  (admin paneli: sipariş/ürün/kullanıcı yönetimi)
    ├── login.html / register.html  (giriş / kayıt)
    ├── css/                        (style.css, login.css)
    ├── js/
    │   ├── api.js                  (API taban adresi, fetch yardımcıları, JWT başlığı)
    │   ├── site.js                 (sayfalar arası ortak: nav, sepet rozeti, sepete ekleme)
    │   ├── admin.js / orders.js    (admin paneli / sipariş geçmişi mantığı)
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
- **Logs**: `Id`, `UserId` (nullable FK → Users), `Action`, `Details`, `Timestamp`

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
| Ürünler | `GET /api/products` (`?search=`), `GET /api/products/{id}`, `POST/PUT/DELETE /api/products` (Admin) |
| Sepet | `GET/POST /api/cart`, `PUT/DELETE /api/cart/{id}` (giriş gerektirir) |
| Siparişler | `POST /api/orders` (checkout), `GET /api/orders` (kendi geçmişi) |
| Admin | `GET /api/admin/orders`, `PUT /api/admin/orders/{id}/approve`, `PUT /api/admin/orders/{id}/reject`, `GET/DELETE /api/admin/users`, `GET /api/admin/logs`, `GET /api/admin/analytics` |

Tüm uç noktaların tam şeması (istek/cevap gövdeleri dahil) için uygulamayı çalıştırıp `/swagger` adresine bakabilirsin.
