using ETicaret.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace ETicaret.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)   // options: "hangi veritabınına, hangi bağlantıyla? bilgisi buraya gelir."
    {
    }

    public DbSet<Role> Roles { get; set; }

    public DbSet<User> Users { get; set; }

    public DbSet<CartItem> CartItems { get; set; }

    public DbSet<Log> Logs { get; set; }

    public DbSet<Category> Categories {get; set;}
    public DbSet<Product> Products {get; set;}
    public DbSet<ProductImage> ProductImages {get; set;}
    public DbSet<Order> Orders {get; set;}
    public DbSet<OrderItem> OrderItems {get; set;}
    public DbSet<Review> Reviews {get; set;}
    public DbSet<Favorite> Favorites {get; set;}
    public DbSet<Coupon> Coupons {get; set;}
    public DbSet<CouponUser> CouponUsers {get; set;}
    public DbSet<WeeklyDealConfig> WeeklyDealConfigs {get; set;}
    public DbSet<MysteryBoxConfig> MysteryBoxConfigs {get; set;}
    public DbSet<MysteryBoxPrize> MysteryBoxPrizes {get; set;}

     protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Para alanlarının veritabanı hassasiyeti: toplam 18 basamak, virgülden sonra 2.
        // Açıkça belirtilmezse EF Core "silently truncated" uyarısı verir.
        modelBuilder.Entity<Product>(entity =>
        {
            entity.Property(p => p.Price).HasPrecision(18, 2);
            entity.Property(p => p.DiscountPrice).HasPrecision(18, 2);
            entity.Property(p => p.Cost).HasPrecision(18, 2);
            // Kod CreatedAt'i açıkça set etse de (yeni ürün eklenirken), bu DB varsayılanı
            // sütun eklenmeden önce var olan satırlara migration anının tarihini yazar —
            // böylece eski ürünler "tarihsiz" (0001-01-01) kalmaz.
            entity.Property(p => p.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        });

        // Kategori kendine referans: bir üst kategorinin birden çok alt kategorisi olabilir.
        // Alt kategorisi olan bir kategori silinmeye çalışılırsa engellenir (Restrict).
        modelBuilder.Entity<Category>(entity =>
        {
            entity.HasOne(c => c.Parent)
                .WithMany(c => c.Children)
                .HasForeignKey(c => c.ParentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<OrderItem>(entity =>
        {
            entity.Property(oi => oi.UnitPrice).HasPrecision(18, 2);
        });

        // Ürün silinince (soft-delete olsa da ileride hard delete ihtimaline karşı) ek görselleri de gitsin
        modelBuilder.Entity<ProductImage>(entity =>
        {
            entity.HasOne(pi => pi.Product)
                .WithMany(p => p.Images)
                .HasForeignKey(pi => pi.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Review>(entity =>
        {
            // Bir kullanıcı bir ürüne tek yorum yazar. Kural sadece controller'da kalsaydı
            // iki isteğin aynı anda gelmesi (çift tıklama) iki yorum bırakabilirdi;
            // benzersiz indeks bunu veritabanı seviyesinde imkânsız kılar.
            entity.HasIndex(r => new { r.ProductId, r.UserId }).IsUnique();

            entity.HasOne(r => r.Product)
                .WithMany(p => p.Reviews)
                .HasForeignKey(r => r.ProductId)
                .OnDelete(DeleteBehavior.Cascade);

            // Kullanıcı silinirse yorumları da gider. (Pratikte yorum yazabilen kullanıcının
            // zaten siparişi vardır ve Orders kısıtı yüzünden silinemez; bu kural yine de
            // yorumu sahipsiz bırakmamak için burada.)
            entity.HasOne(r => r.User)
                .WithMany()
                .HasForeignKey(r => r.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Favorite>(entity =>
        {
            // Aynı ürün bir kullanıcının listesine iki kez giremez. Kalbe hızlı çift tıklama
            // ya da iki sekmeden aynı anda ekleme, kural sadece controller'da kalsaydı
            // listede çift satır bırakabilirdi.
            entity.HasIndex(f => new { f.UserId, f.ProductId }).IsUnique();

            entity.HasOne(f => f.User)
                .WithMany()
                .HasForeignKey(f => f.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(f => f.Product)
                .WithMany(p => p.Favorites)
                .HasForeignKey(f => f.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Coupon>(entity =>
        {
            entity.Property(c => c.Value).HasPrecision(18, 2);
            entity.Property(c => c.MinOrderTotal).HasPrecision(18, 2);

            // Kod tekil olmalı: aynı kodun iki kaydı olsaydı "hangi kupon geçerli"
            // sorusunun cevabı sorgunun sırasına kalırdı.
            entity.HasIndex(c => c.Code).IsUnique();
        });

        modelBuilder.Entity<CouponUser>(entity =>
        {
            // Bileşik anahtar: aynı kupon aynı kişiye iki kez tanımlanamaz.
            entity.HasKey(cu => new { cu.CouponId, cu.UserId });

            entity.HasOne(cu => cu.Coupon)
                .WithMany(c => c.AssignedUsers)
                .HasForeignKey(cu => cu.CouponId)
                .OnDelete(DeleteBehavior.Cascade);

            // Müşteri silinirse ona tanımlanmış haklar da gider. Kuponun kendisi (varsa
            // başka müşterileri) durur; silinen tek şey o kişinin kullanma hakkı.
            entity.HasOne(cu => cu.User)
                .WithMany()
                .HasForeignKey(cu => cu.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Hediye kutusu ödülleri de para taşır (yüzde ya da TL) — kupon alanlarıyla aynı hassasiyet.
        modelBuilder.Entity<MysteryBoxPrize>(entity =>
        {
            entity.Property(p => p.Value).HasPrecision(18, 2);
            entity.Property(p => p.MinOrderTotal).HasPrecision(18, 2);
        });

        modelBuilder.Entity<Order>(entity =>
        {
            entity.Property(o => o.CouponValue).HasPrecision(18, 2);
            entity.Property(o => o.CouponMinOrderTotal).HasPrecision(18, 2);

            // Kuponu silmek, onu kullanmış siparişleri silmemeli (Restrict). Kampanya
            // kapatılmak istenirse IsActive=false yapılır; geçmiş sipariş kaydı korunur.
            entity.HasOne(o => o.Coupon)
                .WithMany(c => c.Orders)
                .HasForeignKey(o => o.CouponId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(o => o.User)
                .WithMany()
                .HasForeignKey(o => o.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(o => o.ApprovedByUser)
                .WithMany()
                .HasForeignKey(o => o.ApprovedBy)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

}

// DbContex C# ile SQL arasındaki tercümanın sözlüğü.