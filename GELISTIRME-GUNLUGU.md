# Geliştirme Günlüğü

Bu dosya, E-Ticaret projesinin geliştirme sürecinde alınan kararları, karşılaşılan hataları ve öğrenilen kavramları kronolojik olarak takip eder. Projenin genel tanıtımı ve kurulum talimatları için [README.md](./README.md)'ye bakın.

## Çalışma şekli

- Kullanıcı (Aslı) tüm gerçek proje kodunu (entity, controller, servis vb.) kendisi yazdı.
- Asistan (Claude) adım adım talimat verdi (hangi dosyaya ne yazılacağını, neden), build ile doğruladı, hataları işaret etti.
- Mekanik/araç işlemleri (paket kurulumu, `dotnet ef` komutları, scaffold, üretilmiş migration dosyalarının ayıklanması gibi öğrenilecek bir kavram içermeyen işler) asistan tarafından doğrudan yapıldı.

## Yapılanlar (kronolojik)

1. **.NET 8 SDK** kuruldu (winget ile — sistemde sadece runtime vardı)
2. **`ETicaret.Api`** ASP.NET Core Web API projesi oluşturuldu, EF Core SQL Server + JWT paketleri kuruldu (net8.0 uyumlu 8.x sürümlerine sabitlendi)
3. Northwind'in var olduğu keşfedildi, hibrit Database-First/Code-First mimarisine karar verildi
4. **Entity'ler** elle yazıldı: `Role.cs`, `User.cs`, `CartItem.cs`, `OrderApproval.cs` (+ `OrderStatus` enum), `Log.cs`
5. **Northwind scaffold** çalıştırıldı (`dotnet ef dbcontext scaffold`) — `Product`, `Order`, `Customer`, `Category`, `OrderDetail`, `Supplier`, `Employee`, `Shipper` vb. otomatik üretildi
6. `CartItem`/`OrderApproval`'a `Product`/`Order` navigation property'leri eklendi
7. **`AppDbContext`**'e 5 yeni tablo (`DbSet`) tanıtıldı, `OrderApproval`'ın `Users` tablosuna çift bağlantısı Fluent API ile yapılandırıldı (`OnDelete(DeleteBehavior.Restrict)`)
8. `appsettings.json`'a **connection string** eklendi, `Program.cs`'e `AddDbContext` kaydı yapıldı
9. **Migration oluşturuldu ve uygulandı** — migration dosyası, Northwind'in var olan tablolarını yeniden oluşturmaya çalışmasın diye elle ayıklandı (sadece 5 yeni tabloyu içerecek şekilde). Northwind veritabanında artık 19 tablo var (13 orijinal + 5 yeni + `__EFMigrationsHistory`)
10. **Auth modülü** tamamlandı: `RegisterDto`, `LoginDto`, `AuthResponseDto`, JWT middleware (`Program.cs`), `AuthService` (Register/Login, şifre hashleme, JWT üretimi), `AuthController` (`/api/auth/register`, `/api/auth/login`) yazıldı, `Roles` tablosuna Admin/Customer seed edildi
11. **Mimari düzeltme:** Northwind kullanımının bir yönlendirme hatası olduğu fark edildi. Northwind'e eklenen 5 tablo geri alındı (migration rollback), Northwind orijinal 13 tabloya döndürüldü. Scaffold edilmiş dosyalar silindi, `OrderApproval` kaldırıldı (alanları `Order`'a taşındı). Yeni, bağımsız **`ETicaretDb`** veritabanı oluşturuldu, connection string güncellendi
12. `Category`, `Product`, `Order` (+ `OrderStatus` enum), `OrderItem` entity'leri yazıldı; `CartItem`'a `Product` navigation'ı geri eklendi
13. `AppDbContext` 8 entity ile güncellendi (`Order`'ın `Users`'a çift bağlantısı yine Fluent API ile ayrıştırıldı), ilk migration oluşturulup `ETicaretDb`'ye uygulandı (8 tablo), `Roles` seed edildi
14. **Eksik kalan `AuthController.cs` ve `Program.cs`'teki `AddScoped<AuthService>()` kaydı** fark edilip tamamlandı (Northwind düzeltmesi arada araya girince atlanmışlardı)
15. **Auth akışı uçtan uca test edildi:** uygulama gerçekten çalıştırıldı, `POST /api/auth/register` ve `POST /api/auth/login` denendi — ikisi de doğru çalışıyor, şifre veritabanında hash'lenmiş halde duruyor, JWT doğru claim'lerle (username, role) üretiliyor. **Auth modülü tamamlandı.**
16. **Ürün modülü tamamlandı ve test edildi:** `CategoryDto`/`ProductDto`, `CategoriesController`, `ProductsController` yazıldı (basit CRUD'da ayrı Service katmanı kullanılmadı, doğrudan `AppDbContext` — kural: gerçek iş mantığı yoksa Controller'ın DbContext'i doğrudan kullanması yeterli). Yol boyunca birkaç kayda değer hata bulunup düzeltildi:
    - `AuthController.cs` ve `Program.cs`'teki `AddScoped<AuthService>()` kaydı hiç yapılmamıştı (Northwind düzeltmesi arada araya girmişti) — eklendi
    - `CategoriesController.cs`'e yanlışlıkla `[ApiController]`/`[Route]` etiketlerinin hemen altına ikinci bir `CategoryDto` sınıfı yapıştırılmıştı — bu, o etiketlerin yanlış sınıfa (controller yerine DTO'ya) uygulanmasına, dolayısıyla route'ların tamamen kaybolmasına yol açtı
    - `Product.cs`'e `Description` alanı migration'dan **sonra** eklenmişti — veritabanı tablosunda karşılığı olmadığı için "Invalid column name" hatası alındı, yeni bir migration (`AddProductDescription`) ile düzeltildi
    - Uygulama gerçekten çalıştırılıp ürün ekleme/listeleme/arama/güncelleme/silme + yetkisiz erişim reddi (401) canlı test edildi, hepsi doğru çalışıyor. **Ürün modülü tamamlandı.**
17. **Sepet ve Sipariş modülü tamamlandı ve test edildi:** `CartItemDto`, `CartController` (görüntüle/ekle/güncelle/sil — kullanıcı sadece kendi sepetine erişebiliyor, `UserId == CurrentUserId` kontrolüyle IDOR açığı engellendi), `Order.cs`'e `OrderItems` koleksiyonu eklendi, `OrderService` (checkout iş mantığı: sepetten sipariş oluşturma, fiyat anlık görüntüsü alma, sepeti boşaltma — hepsi tek transaction'da), `OrdersController` (`POST /api/orders` checkout, `GET /api/orders` geçmiş) yazıldı. Yol boyunca bulunan hatalar:
    - `Order`/`OrderItem` arasındaki çift yönlü ilişki JSON'a çevrilirken sonsuz döngüye giriyordu (`Order → OrderItems → Order → ...`) — `Program.cs`'te `ReferenceHandler.IgnoreCycles` ile düzeltildi
    - Uygulama çalıştırılıp tam akış (sepete ekle → sipariş ver → sepetin boşaldığını doğrula → geçmişte gör) canlı test edildi, stoğun onay öncesi düşmediği doğrulandı. **Sepet/Sipariş modülü tamamlandı.**
18. **Admin paneli tamamlandı ve test edildi:** `OrderService`'e `ApproveOrderAsync`/`RejectOrderAsync` (çifte onay/red'e karşı `Status != Pending` koruması ile) ve `GetAllOrdersAsync` eklendi; `AdminController` yazıldı (`api/admin/orders` — listele/onayla/reddet, `api/admin/users` — listele/sil, tümü `[Authorize(Roles = "Admin")]`). Bulunan ve düzeltilen önemli bir güvenlik açığı:
    - `GET /api/admin/orders` cevabında, `Order.User` navigation'ı üzerinden **`PasswordHash` dışarı sızıyordu** — `UserSummaryDto` sadece `GetAllUsers` endpoint'ini koruyordu, başka bir yoldan (Order→User) yine sızabiliyordu. Kalıcı çözüm: `User.PasswordHash`'e `[JsonIgnore]` eklenerek bu alanın **hiçbir zaman, hiçbir endpoint'ten** JSON'a dönmemesi garanti altına alındı
    - `DeleteUser`'da veritabanı kısıtlama hatası (siparişi olan kullanıcıyı silme girişimi) `DbUpdateException` ile yakalanıp temiz bir `400` mesajına çevrildi
    - Uygulama çalıştırılıp tüm akış canlı test edildi: sipariş onayında stok doğru düşüyor, reddedilende düşmüyor, çifte onay engelleniyor, kullanıcı silme koruması ve `passwordHash` sızıntısının kapandığı doğrulandı. **Admin paneli tamamlandı.**
19. **Log ve Analiz modülü tamamlandı ve test edildi:** `LoggingMiddleware` yazıldı (ilk custom middleware — `RequestDelegate` zinciri, `InvokeAsync`'e method-injection ile `AppDbContext` alma, sadece değişiklik yapan (`GET` dışı) istekleri loglama), `Program.cs`'te `UseAuthorization()` ile `MapControllers()` arasına kaydedildi. `AdminController`'a `GET /api/admin/logs` (son 100 log) ve `GET /api/admin/analytics` (toplam/onaylı/bekleyen/reddedilen sipariş sayısı, toplam gelir, en çok satan ürün — `GroupBy` ile) eklendi. Test sırasında middleware kaydının (`app.UseMiddleware<LoggingMiddleware>()`) unutulduğu fark edilip düzeltildi. Uygulama çalıştırılıp loglamanın (giriş yapmış/yapmamış senaryolarda `UserId` doğru dolup dolmadığı dahil) ve analiz sonuçlarının doğruluğu canlı test edildi. **Log/Analiz modülü tamamlandı — backend'in planlanan tüm modülleri bitti.**
20. **Gizli bilgiler güvenli hale getirildi:** `appsettings.json`'daki JWT anahtarı ve connection string, `.NET User Secrets`'a taşındı (`dotnet user-secrets init/set`), `appsettings.json`'da boş bırakıldı. `.gitignore` oluşturuldu. Commit öncesi iki dosya adı tutarsızlığı bulundu ve düzeltildi (`Services/Order.cs` → `OrderService.cs`, `Models/Dtos/CartItem.cs` → `CartItemDto.cs`). Git repository kuruldu, ilk commit atıldı, GitHub'a push edildi (`https://github.com/aslihan05/e-ticaret`).

## Öğrenilen kavramlar (sözlük)

- **Entity**: Bir veritabanı tablosunu temsil eden C# sınıfı (ORM'in temel yapı taşı)
- **DTO (Data Transfer Object)**: API'ye gelen/giden veriyi taşıyan, entity'den ayrı sade sınıf — hassas alanları (örn. `PasswordHash`) dışarı sızdırmamak için kullanılır
- **Code-First**: Önce C# sınıfı yazılır, veritabanı şeması ondan otomatik üretilir (migration ile)
- **Database-First**: Önce veritabanı şeması vardır, C# sınıfları ondan otomatik üretilir (scaffold ile)
- **Migration**: Code-First modelindeki değişikliklerin veritabanına uygulanacak SQL komutlarına dönüştürülmesi, versiyonlanmış şekilde
- **Fluent API**: EF Core'da ilişkileri/kısıtlamaları property attribute'ları yerine `OnModelCreating` içinde kod ile tanımlama yöntemi (özellikle EF Core'un otomatik çıkaramadığı, örn. aynı tabloya çift FK gibi durumlarda gerekli)
- **DI (Dependency Injection)**: ASP.NET Core'un, `AppDbContext` gibi nesneleri ihtiyaç duyan yerlere otomatik "enjekte etmesi" — `new` ile elle oluşturmaya gerek kalmaz
- **Authentication vs Authorization**: Authentication "sen kimsin" (kimlik doğrulama), Authorization "bunu yapmaya iznin var mı" (yetkilendirme)
- **JWT (JSON Web Token)**: Login sonrası verilen, imzalı/stateless kimlik token'ı — sunucu her istekte veritabanına gitmeden kullanıcıyı doğrulayabilir
- **Middleware**: Her HTTP isteğinin üzerinden geçtiği, sırayla çalışan pipeline parçaları (`UseAuthentication`, `UseAuthorization`, kendi yazdığımız `LoggingMiddleware` gibi)
- **IDOR (Insecure Direct Object Reference)**: Bir kullanıcının, sahibi olmadığı bir kaydın ID'sini tahmin ederek erişebilmesi güvenlik açığı — `CartController`'da `UserId == CurrentUserId` kontrolüyle engellendi
- **User Secrets**: .NET'in geliştirme ortamında gizli bilgileri (API anahtarları, connection string) proje dizini dışında, git'e girmeyecek şekilde saklama mekanizması
