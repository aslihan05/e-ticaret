# E-Ticaret (Çakma)

Öğrenme amaçlı, uçtan uca çalışan bir e-ticaret backend projesi. Kullanıcı kaydı/girişi, ürün kataloğu, sepet ve sipariş akışı, admin onay/stok yönetimi ile log ve analiz özelliklerini içerir.

## Özellikler

- **Kullanıcı sistemi**: Kayıt, giriş, JWT tabanlı kimlik doğrulama, rol bazlı yetkilendirme (Admin / Customer)
- **Ürün kataloğu**: Kategori ve ürün yönetimi (admin), listeleme ve arama (herkese açık)
- **Sepet ve sipariş**: Sepete ekleme/güncelleme/silme, sepetten sipariş oluşturma (checkout)
- **Admin paneli**: Sipariş onaylama/reddetme (onayda otomatik stok düşümü), kullanıcı listeleme/silme
- **Log ve analiz**: Değişiklik yapan isteklerin otomatik loglanması, temel satış istatistikleri (toplam gelir, en çok satan ürün)

## Teknoloji yığını

| Katman | Seçim |
|---|---|
| Backend | .NET 8 / ASP.NET Core Web API (C#) |
| Veritabanı | SQL Server |
| ORM | Entity Framework Core 8 (Code-First) |
| Kimlik doğrulama | JWT (JSON Web Token) |
| Frontend | Düz HTML/CSS/JavaScript (geliştirme aşamasında) |

## Proje yapısı

```
E-Ticaret/
├── README.md
├── GELISTIRME-GUNLUGU.md          (geliştirme süreci, kararlar, kronolojik notlar)
├── backend/
│   └── ETicaret.Api/
│       ├── Controllers/            (Auth, Products, Categories, Cart, Orders, Admin)
│       ├── Models/
│       │   ├── Entities/           (Role, User, Category, Product, CartItem, Order, OrderItem, Log)
│       │   └── Dtos/
│       ├── Data/
│       │   └── AppDbContext.cs
│       ├── Migrations/
│       ├── Services/                (AuthService, OrderService)
│       ├── Middleware/               (LoggingMiddleware)
│       ├── Program.cs
│       └── appsettings.json
└── frontend/                        (henüz oluşturulmadı)
```

## Veritabanı şeması

- **Roles**: `Id`, `Name` (Admin / Customer)
- **Users**: `Id`, `Username`, `PasswordHash`, `CreatedAt`, `CreatedBy`, `RoleId` (FK → Roles)
- **Categories**: `Id`, `Name`
- **Products**: `Id`, `Name`, `Description`, `Price`, `Stock`, `CategoryId` (FK), `ImageUrl`, `IsActive`
- **CartItems**: `Id`, `UserId` (FK → Users), `ProductId` (FK → Products), `Quantity`
- **Orders**: `Id`, `UserId` (FK → Users), `Status` (Pending / Approved / Rejected), `CreatedAt`, `ApprovedAt`, `ApprovedBy` (FK → Users)
- **OrderItems**: `Id`, `OrderId` (FK), `ProductId` (FK), `Quantity`, `UnitPrice` (sipariş anındaki fiyatın anlık görüntüsü)
- **Logs**: `Id`, `UserId` (nullable FK → Users), `Action`, `Details`, `Timestamp`

**Not:** `Users` tablosu hem giriş hesaplarını hem "müşteri" kavramını karşılar — ayrı bir `Customers` tablosu yoktur. **Kullanıcı** rolü (`Role = Customer`) müşteriyi, `Role = Admin` yöneticiyi temsil eder.

**İş kuralı:** Bir sipariş onaylandığında (`Order.Status = Approved`), ilgili `OrderItems` satırlarındaki her ürün için `Product.Stock` düşürülür. Onay öncesi (`Pending`) veya reddedilen (`Rejected`) siparişlerde stok değişmez.

## Kurulum ve çalıştırma

### Gereksinimler

- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- SQL Server (Windows Authentication ile erişim varsayılır — connection string'i ortamına göre değiştirebilirsin)

### Adımlar

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

5. **Başlangıç rollerini ekle** (Users tablosu bir role bağlı olmak zorunda olduğu için gerekli)
   ```sql
   INSERT INTO Roles (Name) VALUES ('Admin'), ('Customer');
   ```

6. **Uygulamayı çalıştır**
   ```bash
   dotnet run
   ```
   Varsayılan olarak `https://localhost:xxxx` (veya `--urls` ile belirtilen adreste) ayağa kalkar. `/swagger` yoluyla tüm API uç noktalarını interaktif olarak deneyebilirsin.

7. **İlk admin kullanıcıyı oluştur:** `POST /api/auth/register` ile bir kullanıcı kaydet (varsayılan olarak `Customer` rolüyle oluşur), sonra veritabanında o kullanıcının `RoleId`'sini `1` (Admin) olarak güncelle:
   ```sql
   UPDATE Users SET RoleId = 1 WHERE Username = 'kullanici_adin';
   ```

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

## Geliştirme süreci

Bu proje, adım adım rehberlikle ve süreç boyunca canlı testlerle geliştirildi. Alınan mimari kararların gerekçeleri, karşılaşılan hatalar ve çözümleri, ve öğrenilen kavramların bir dökümü için [GELISTIRME-GUNLUGU.md](./GELISTIRME-GUNLUGU.md) dosyasına bakabilirsin.
