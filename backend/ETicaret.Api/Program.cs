using ETicaret.Api.Data;
using Microsoft.EntityFrameworkCore;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Threading.RateLimiting;
using ETicaret.Api.Services;
using ETicaret.Api.Middleware;
using ETicaret.Api.Models.Entities;


var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;

        // Tüm tarihler JSON'a UTC olarak ("Z" ekiyle) yazılır. Aksi halde veritabanından
        // Kind=Unspecified dönen tarihleri tarayıcı yerel saat sanıp 3 saat kaydırıyor.
        // Bkz. Models/UtcDateTimeConverter.cs
        options.JsonSerializerOptions.Converters.Add(new ETicaret.Api.Models.UtcDateTimeConverter());
        options.JsonSerializerOptions.Converters.Add(new ETicaret.Api.Models.UtcNullableDateTimeConverter());
    });
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));  // Herhangi bir controller "Constructorum da AppDbContext istiyorum" dediğinde ASP.NET Core onu hazır edip verir.
    
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<OrderService>();
builder.Services.AddScoped<StockService>();
builder.Services.AddScoped<CouponService>();

// Chatbot: araçları çalıştıran katman (scoped, AppDbContext'e bağlı) ve Claude API'yi
// çağıran servis (typed HttpClient — soket tükenmesine karşı framework yönetir).
builder.Services.AddScoped<ChatbotTools>();
builder.Services.AddHttpClient<ChatbotService>();

// AddHttpClient: EmailService'e yönetimi framework'e ait bir HttpClient verir.
// (HttpClient'ı elle new'lemek soket tükenmesine yol açan bilinen bir hatadır.)
builder.Services.AddHttpClient<EmailService>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        // Geliştirmede frontend'in portu (Live Server, http-server, file://) değişkendir;
        // sabit bir liste her port değişiminde "Failed to fetch" olarak geri döner.
        // Kimlik doğrulama cookie ile değil Authorization başlığıyla taşındığı için
        // origin'i serbest bırakmak burada oturum çalmaya açık kapı bırakmaz.
        if (builder.Environment.IsDevelopment())
            policy.AllowAnyOrigin();
        else
            policy.WithOrigins("http://localhost:5500", "http://127.0.0.1:5500",
                               "http://localhost:5501", "http://127.0.0.1:5501");

        policy.AllowAnyHeader().AllowAnyMethod();
    });
});
// Brute force koruması: şifre deneme uçlarına (giriş/kayıt) IP başına hız sınırı.
// Sınırsız deneme, zayıf şifreli bir hesabın er ya da geç kırılması demektir.
//
// Sınır neden 30, daha düşük değil? Kova IP başına ayrılıyor; ama aynı IP'nin arkasında
// TEK kullanıcı olduğu varsayılamaz: bir ofis, okul ya da mobil operatör NAT'ı yüzlerce
// kullanıcıyı tek IP'den çıkarır ve hepsi bu kovayı paylaşır. Sınır dar tutulursa
// meşru kullanıcılar birbirini kilitler — koruma değil, kendi kendine arıza olur.
// 30/dk bir insanı asla rahatsız etmez, ama sözlük saldırısını (dakikada binlerce deneme
// gerekir) pratikte imkânsız kılar.
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("AuthLimit", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            // Aynı IP'den gelen istekler aynı kovaya düşer
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "bilinmeyen",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0   // Sıraya alma, doğrudan reddet
            }));

    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.HttpContext.Response.WriteAsJsonAsync(
            new { message = "Çok fazla deneme yaptınız. Lütfen bir dakika sonra tekrar deneyin." }, token);
    };
});

var jwtKey = builder.Configuration["Jwt:Key"]!;

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidAudience = builder.Configuration["Jwt:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
    };

    // JWT "stateless"tir: bir kez imzalandıktan sonra süresi dolana kadar (120 dk) geçerlidir.
    // Bloklamayı yalnızca login'de kontrol etmek yetmez — zaten giriş yapmış bir kullanıcı
    // bloklandıktan sonra da elindeki token'la istek atmaya devam edebilirdi.
    // Token imzası doğrulandıktan sonra kullanıcının hâlâ bloklu olup olmadığı veritabanından
    // kontrol edilir; bloklu (ya da silinmiş) ise istek 401 ile reddedilir.
    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = async context =>
        {
            var userIdClaim = context.Principal?.FindFirst("UserId")?.Value;
            if (!int.TryParse(userIdClaim, out int userId))
            {
                context.Fail("Geçersiz token.");
                return;
            }

            var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
            var user = await db.Users
                .AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => new { u.IsBlocked })
                .FirstOrDefaultAsync();

            if (user == null || user.IsBlocked)
            {
                context.Fail("Hesap engellenmiş veya bulunamadı.");
            }
        }
    };
});

var app = builder.Build();

// Configure the HTTP request pipeline.

app.UseSwagger();
app.UseSwaggerUI();


// Yerel geliştirmede HTTP kullanıyoruz; HTTPS portu tanımlı olmadığı için
// UseHttpsRedirection sadece uyarı üretiyordu, bu yüzden kaldırıldı.
app.UseCors("Frontend");
app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<LoggingMiddleware>();
app.UseAuthorization();

app.MapControllers();

app.Run();
