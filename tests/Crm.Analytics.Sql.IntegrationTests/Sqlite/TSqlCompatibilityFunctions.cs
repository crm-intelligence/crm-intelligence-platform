using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Crm.Analytics.Sql.IntegrationTests.Sqlite;

/// <summary>
/// SQLite'ta bulunmayan T-SQL fonksiyonlarini baglanti uzerinde kullanici
/// tanimli fonksiyon (UDF) olarak kaydeder.
/// </summary>
/// <remarks>
/// <para>
/// Neden UDF, neden SQL cevirisi degil: anlam C#'ta ifade edildiginde dogru
/// olmasi kolay, SQL'e cevrildiginde ise sessizce yanlis olmasi kolaydir.
/// En belirgin ornek <see cref="DateDiff"/> aciklamasindadir.
/// </para>
/// <para>
/// Tum fonksiyonlar <c>isDeterministic: true</c> ile kaydedilir; aksi halde
/// SQLite sorgu planlayicisi bunlari <c>GROUP BY</c> icinde kabul etmez.
/// </para>
/// </remarks>
internal static class TSqlCompatibilityFunctions
{
    /// <summary>
    /// Fixture'daki tarih kolonlarinin biciminde saklanan degerler
    /// (<c>order_purchase_timestamp</c> ISO 8601 metin).
    /// </summary>
    private static readonly string[] DateTimeFormats =
    [
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd"
    ];

    public static void Register(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        connection.CreateFunction<string?, int?>(
            "YEAR", v => Component(v, d => d.Year), isDeterministic: true);

        connection.CreateFunction<string?, int?>(
            "MONTH", v => Component(v, d => d.Month), isDeterministic: true);

        connection.CreateFunction<string?, int?>(
            "DAY", v => Component(v, d => d.Day), isDeterministic: true);

        connection.CreateFunction<string?, string?, int?>(
            "DATEPART", DatePart, isDeterministic: true);

        connection.CreateFunction<string?, string?, string?, int?>(
            "DATEDIFF", DateDiff, isDeterministic: true);

        connection.CreateFunction<string?, int?, string?, string?>(
            "DATEADD", DateAdd, isDeterministic: true);

        connection.CreateFunction<string?, string?>(
            "EOMONTH", v => EndOfMonth(v, 0), isDeterministic: true);

        connection.CreateFunction<string?, int?>(
            "LEN", Len, isDeterministic: true);
    }

    // -----------------------------------------------------------------------

    private static int? Component(string? value, Func<DateTime, int> selector)
    {
        return value is null ? null : selector(Parse(value));
    }

    private static int? DatePart(string? part, string? value)
    {
        if (part is null || value is null)
        {
            return null;
        }

        var date = Parse(value);

        return part.ToLowerInvariant() switch
        {
            "year" => date.Year,
            // T-SQL ceyregi 1..4 dondurur.
            "quarter" => ((date.Month - 1) / 3) + 1,
            "month" => date.Month,
            "day" => date.Day,
            "dayofyear" => date.DayOfYear,
            "hour" => date.Hour,
            "minute" => date.Minute,
            "second" => date.Second,
            // SqliteFunctionRewriter bilinmeyen parcalari zaten reddediyor;
            // buraya dusmek harness'in kendi ic tutarsizligi demektir.
            _ => throw new InvalidOperationException(
                $"DATEPART('{part}', ...) UDF tarafinda taninmiyor. " +
                "SqliteFunctionRewriter.KnownDateParts ile bu switch ayristi.")
        };
    }

    /// <summary>
    /// T-SQL <c>DATEDIFF</c> semantigi: SINIR GECISI sayar, gecen sureyi degil.
    /// </summary>
    /// <remarks>
    /// Bu ayrim, ceviricilerin sessizce yanlisa saptigi klasik yerdir.
    /// <c>('2018-01-01 23:00', '2018-01-02 01:00')</c> ornegi:
    /// <list type="bullet">
    ///   <item>T-SQL <c>DATEDIFF(day, ...)</c> = <b>1</b> (bir gun siniri gecildi),</item>
    ///   <item><c>julianday(b) - julianday(a)</c> = <b>0</b> (arada 2 saat var).</item>
    /// </list>
    /// C#'ta tarih bilesenlerini kirparak karsilastirmak dogru sonucu dogal
    /// olarak verir.
    /// </remarks>
    private static int? DateDiff(string? part, string? start, string? end)
    {
        if (part is null || start is null || end is null)
        {
            return null;
        }

        var a = Parse(start);
        var b = Parse(end);

        return part.ToLowerInvariant() switch
        {
            "year" => b.Year - a.Year,
            "quarter" => ((b.Year - a.Year) * 4) + (((b.Month - 1) / 3) - ((a.Month - 1) / 3)),
            "month" => ((b.Year - a.Year) * 12) + (b.Month - a.Month),
            "day" => (b.Date - a.Date).Days,
            "dayofyear" => (b.Date - a.Date).Days,
            "hour" => (int)(Truncate(b, TimeSpan.FromHours(1)) - Truncate(a, TimeSpan.FromHours(1))).TotalHours,
            "minute" => (int)(Truncate(b, TimeSpan.FromMinutes(1)) - Truncate(a, TimeSpan.FromMinutes(1))).TotalMinutes,
            "second" => (int)(Truncate(b, TimeSpan.FromSeconds(1)) - Truncate(a, TimeSpan.FromSeconds(1))).TotalSeconds,
            _ => throw new InvalidOperationException(
                $"DATEDIFF('{part}', ...) UDF tarafinda taninmiyor.")
        };
    }

    private static DateTime Truncate(DateTime value, TimeSpan resolution)
    {
        return value.AddTicks(-(value.Ticks % resolution.Ticks));
    }

    private static string? DateAdd(string? part, int? amount, string? value)
    {
        if (part is null || amount is null || value is null)
        {
            return null;
        }

        var date = Parse(value);

        var result = part.ToLowerInvariant() switch
        {
            "year" => date.AddYears(amount.Value),
            "quarter" => date.AddMonths(amount.Value * 3),
            "month" => date.AddMonths(amount.Value),
            "day" or "dayofyear" => date.AddDays(amount.Value),
            "hour" => date.AddHours(amount.Value),
            "minute" => date.AddMinutes(amount.Value),
            "second" => date.AddSeconds(amount.Value),
            _ => throw new InvalidOperationException(
                $"DATEADD('{part}', ...) UDF tarafinda taninmiyor.")
        };

        return Format(result);
    }

    private static string? EndOfMonth(string? value, int monthsToAdd)
    {
        if (value is null)
        {
            return null;
        }

        var date = Parse(value).AddMonths(monthsToAdd);

        // T-SQL EOMONTH yalnizca tarih dondurur.
        return new DateTime(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month))
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// T-SQL <c>LEN</c> semantigi: SONDAKI BOSLUKLARI SAYMAZ.
    /// </summary>
    /// <remarks>
    /// SQLite'in <c>LENGTH</c> fonksiyonu sondaki bosluklari sayar. Bu farkin
    /// UDF ile kapatilmasi, <c>LEN</c>'i <c>LENGTH</c>'e yeniden adlandirmaktan
    /// daha dogru: sehir adlarinda sonda bosluk bulunan bir veri setinde iki
    /// fonksiyon farkli sonuc verirdi.
    /// </remarks>
    private static int? Len(string? value)
    {
        return value?.TrimEnd(' ').Length;
    }

    /// <summary>
    /// Tarih metnini ayristirir. BASARISIZLIKTA ISTISNA ATAR.
    /// </summary>
    /// <remarks>
    /// <c>null</c> dondurmek cazip ama YANLIS olurdu: NULL donen bir tarih
    /// fonksiyonu <c>GROUP BY</c> icinde tum satirlari tek bir gruba toplar ve
    /// test yesil kalirken sayi yanlis cikar. Beklenmeyen bicim, sessizce
    /// yutulacak bir sey degil.
    /// </remarks>
    private static DateTime Parse(string value)
    {
        if (DateTime.TryParseExact(
                value,
                DateTimeFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            return parsed;
        }

        throw new InvalidOperationException(
            $"Tarih degeri beklenen bicimde degil: '{value}'. " +
            $"Beklenen bicimler: {string.Join(" | ", DateTimeFormats)}. " +
            "Fixture'daki kolon tipi degistiyse TSqlCompatibilityFunctions " +
            "guncellenmelidir.");
    }

    private static string Format(DateTime value)
    {
        // Sonucun TEXT kolonlariyla sozluksel olarak karsilastirilabilir
        // kalmasi icin ayni ISO bicimi kullanilir.
        return value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }
}
