using System.Text.Json;
using System.Text.Json.Serialization;

namespace ETicaret.Api.Models;

// Neden gerekli?
//
// Bu projede tarihler DateTime.UtcNow ile UTC olarak kaydedilir. Ama SQL Server'daki
// datetime2 sütununda saat dilimi bilgisi TUTULMAZ; EF Core veriyi geri okuduğunda
// DateTime.Kind = Unspecified olur. System.Text.Json, Kind=Unspecified olan bir tarihi
// JSON'a sonunda "Z" (=UTC) işareti OLMADAN yazar:
//
//      "2026-07-16T11:33:48"        <- işaretsiz
//
// Tarayıcıdaki new Date("2026-07-16T11:33:48") bunu YEREL saat kabul eder. Türkiye
// UTC+3 olduğu için, UTC 11:33'te verilen sipariş ekranda 11:33 yerine 14:33 olarak
// gösterilmesi gerekirken 11:33 kalır — yani tüm tarihler 3 saat geride görünür.
// Aynı sorun geri sayım gibi hesaplarda süreyi tamamen yanlış hesaplatır.
//
// Çözüm: Kind bilgisi ne olursa olsun, yazarken tarihi UTC olarak işaretle ("Z" ekle).
// Bu, "veritabanındaki bütün tarihler UTC'dir" kuralımızın JSON'a yansımasıdır.
public class UtcDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // Gelen tarih (örn. indirim başlangıcı) "Z" ile geldiyse UTC'ye çevrilir,
        // gelmediyse olduğu gibi bırakılır — girişte davranışı değiştirmiyoruz.
        return reader.GetDateTime();
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        var utc = value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)   // Veritabanından gelen: zaten UTC, sadece etiketle
            : value.ToUniversalTime();                         // Local ise gerçekten çevir

        writer.WriteStringValue(utc.ToString("o"));   // ISO 8601, sonunda "Z" ile
    }
}

// Nullable tarihler (DateTime?) için aynı davranış — örn. ApprovedAt, DiscountEnd.
public class UtcNullableDateTimeConverter : JsonConverter<DateTime?>
{
    public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        return reader.GetDateTime();
    }

    public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
            return;
        }

        var v = value.Value;
        var utc = v.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(v, DateTimeKind.Utc)
            : v.ToUniversalTime();

        writer.WriteStringValue(utc.ToString("o"));
    }
}
