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
    public DbSet<Order> Orders {get; set;}
    public DbSet<OrderItem> OrderItems {get; set;}

     protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Para alanlarının veritabanı hassasiyeti: toplam 18 basamak, virgülden sonra 2.
        // Açıkça belirtilmezse EF Core "silently truncated" uyarısı verir.
        modelBuilder.Entity<Product>(entity =>
        {
            entity.Property(p => p.Price).HasPrecision(18, 2);
            entity.Property(p => p.DiscountPrice).HasPrecision(18, 2);
        });

        modelBuilder.Entity<OrderItem>(entity =>
        {
            entity.Property(oi => oi.UnitPrice).HasPrecision(18, 2);
        });

        modelBuilder.Entity<Order>(entity =>
        {
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