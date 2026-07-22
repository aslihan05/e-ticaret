using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using ETicaret.Api.Models.Dtos;

namespace ETicaret.Api.Services;

// Tüm entity'ler için ortak, dinamik ve çoklu-kolon filtreleme altyapısı.
//
// Neden Expression Tree? Filtre kuralları ÇALIŞMA ZAMANINDA (frontend'den) gelir; hangi alan,
// hangi operatör önceden bilinmez. Elle yazılan `Where(p => p.Name.Contains(x))` bunu karşılamaz.
// Burada her kural için reflection ile alan bulunur ve `x => x.Alan OP değer` ifadesi
// PROGRAMLI olarak kurulur. Sonuç yine IQueryable olduğu için filtre VERİTABANINDA çalışır —
// on binlerce kayıt RAM'e çekilmez.
public static class QueryableFilterExtensions
{
    public static IQueryable<T> ApplyFilters<T>(this IQueryable<T> source, IEnumerable<FilterRule>? rules)
    {
        if (rules == null) return source;

        foreach (var rule in rules)
        {
            if (rule?.Field is null || string.IsNullOrWhiteSpace(rule.Field)) continue;
            if (string.IsNullOrWhiteSpace(rule.Value)) continue;   // boş değer = o kural yok sayılır

            var predicate = TryBuildPredicate<T>(rule);
            if (predicate != null) source = source.Where(predicate);
        }
        return source;
    }

    // Dinamik sıralama: sortBy alan adı, sortDir "asc"/"desc".
    public static IQueryable<T> ApplySort<T>(this IQueryable<T> source, string? sortBy, string? sortDir)
    {
        if (string.IsNullOrWhiteSpace(sortBy)) return source;

        var param = Expression.Parameter(typeof(T), "x");
        Expression body;
        try { body = ResolveMember(param, typeof(T), sortBy, out _); }
        catch { return source; }   // bilinmeyen alan: sıralamayı yok say

        var keySelector = Expression.Lambda(body, param);
        bool desc = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);
        string method = desc ? "OrderByDescending" : "OrderBy";

        var call = Expression.Call(typeof(Queryable), method,
            new[] { typeof(T), body.Type }, source.Expression, Expression.Quote(keySelector));
        return source.Provider.CreateQuery<T>(call);
    }

    private static Expression<Func<T, bool>>? TryBuildPredicate<T>(FilterRule rule)
    {
        var param = Expression.Parameter(typeof(T), "x");

        Expression member;
        List<Expression> nullGuards;
        try { member = ResolveMember(param, typeof(T), rule.Field!, out nullGuards); }
        catch { return null; }   // bilinmeyen alan: kuralı sessizce atla (güvenlik + dayanıklılık)

        var comparison = BuildComparison(member, rule);
        if (comparison == null) return null;

        // Ara navigasyonlar null olabilir (x.Category.Name): önce "x.Category != null" ile guard'la.
        foreach (var guard in nullGuards) comparison = Expression.AndAlso(guard, comparison);

        return Expression.Lambda<Func<T, bool>>(comparison, param);
    }

    // "Category.Name" gibi noktalı yolu adım adım çözer; ara referans-tip adımlar için null guard toplar.
    private static Expression ResolveMember(Expression root, Type type, string path, out List<Expression> nullGuards)
    {
        nullGuards = new List<Expression>();
        Expression current = root;
        Type currentType = type;

        var parts = path.Split('.');
        for (int i = 0; i < parts.Length; i++)
        {
            var prop = currentType.GetProperty(parts[i],
                           BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.Instance)
                       ?? throw new ArgumentException($"Bilinmeyen alan: {parts[i]}");

            current = Expression.Property(current, prop);
            currentType = prop.PropertyType;

            if (i < parts.Length - 1 && !currentType.IsValueType)
                nullGuards.Add(Expression.NotEqual(current, Expression.Constant(null, currentType)));
        }
        return current;
    }

    private static Expression? BuildComparison(Expression member, FilterRule rule)
    {
        var memberType = member.Type;
        var underlying = Nullable.GetUnderlyingType(memberType) ?? memberType;
        string op = (rule.Op ?? "").Trim().ToLowerInvariant();
        string raw = rule.Value!.Trim();

        // ----- string -----
        if (underlying == typeof(string))
        {
            var val = Expression.Constant(raw, typeof(string));
            var notNull = Expression.NotEqual(member, Expression.Constant(null, typeof(string)));
            if (op == "eq") return Expression.Equal(member, val);
            if (op == "neq") return Expression.NotEqual(member, val);
            // varsayılan: contains (SQL'de LIKE '%..%', SQL Server'da büyük/küçük harf duyarsız)
            var contains = Expression.Call(member, nameof(string.Contains), Type.EmptyTypes, val);
            return Expression.AndAlso(notNull, contains);
        }

        // ----- bool -----
        if (underlying == typeof(bool))
        {
            if (!bool.TryParse(raw, out var b)) return null;
            return Compare(member, memberType, Expression.Constant(b), op == "neq" ? "neq" : "eq");
        }

        // ----- enum -----
        if (underlying.IsEnum)
        {
            object? enumVal = null;
            if (int.TryParse(raw, out var ei) && Enum.IsDefined(underlying, ei))
                enumVal = Enum.ToObject(underlying, ei);
            else if (Enum.TryParse(underlying, raw, ignoreCase: true, out var ev))
                enumVal = ev;
            if (enumVal == null) return null;
            return Compare(member, memberType, Expression.Constant(enumVal, underlying), op == "neq" ? "neq" : "eq");
        }

        // ----- DateTime -----
        if (underlying == typeof(DateTime))
        {
            if (!TryParseDate(raw, out var dt)) return null;

            if (op == "to")
            {
                // "to" seçilen günün SONUNA kadar (23:59:59.999) dahil eder
                var end = dt.Date.AddDays(1).AddTicks(-1);
                return Compare(member, memberType, Expression.Constant(end, typeof(DateTime)), "lte");
            }
            if (op == "from" || op == "")
            {
                return Compare(member, memberType, Expression.Constant(dt.Date, typeof(DateTime)), "gte");
            }
            return Compare(member, memberType, Expression.Constant(dt, typeof(DateTime)), op);
        }

        // ----- sayısal -----
        if (underlying == typeof(int) || underlying == typeof(long) || underlying == typeof(decimal)
            || underlying == typeof(double) || underlying == typeof(float) || underlying == typeof(short))
        {
            object num;
            try { num = Convert.ChangeType(raw, underlying, CultureInfo.InvariantCulture); }
            catch { return null; }
            return Compare(member, memberType, Expression.Constant(num, underlying), op == "" ? "eq" : op);
        }

        return null;   // desteklenmeyen tip: kuralı atla
    }

    // member (memberType; nullable olabilir) ile underlying tipinde bir sabiti karşılaştırır.
    private static Expression Compare(Expression member, Type memberType, Expression constUnderlying, string op)
    {
        // member nullable ise (decimal?, DateTime?...) sabiti de nullable'a çevir ki tipler eşleşsin.
        Expression right = Nullable.GetUnderlyingType(memberType) != null
            ? Expression.Convert(constUnderlying, memberType)
            : constUnderlying;

        return op switch
        {
            "eq" => Expression.Equal(member, right),
            "neq" => Expression.NotEqual(member, right),
            "gt" => Expression.GreaterThan(member, right),
            "gte" => Expression.GreaterThanOrEqual(member, right),
            "lt" => Expression.LessThan(member, right),
            "lte" => Expression.LessThanOrEqual(member, right),
            _ => Expression.Equal(member, right)
        };
    }

    private static bool TryParseDate(string raw, out DateTime dt)
    {
        // Tarihler UTC tutulur; gelen değeri UTC varsayarak çöz.
        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out dt))
            return true;
        if (DateTime.TryParse(raw, out dt))
        {
            dt = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
            return true;
        }
        return false;
    }
}
