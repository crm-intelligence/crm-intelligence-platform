namespace Crm.Analytics.Sql.IntegrationTests.Sqlite;

/// <summary>
/// Allow-list'teki T-SQL fonksiyonlarinin SQLite karsisindaki UC kategoriye
/// bolunmesi. Bu bolumleme harness'in tek dogruluk kaynagidir.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="FunctionCoverageTests"/> bu uc kumenin birlesiminin
/// <c>AllowListDocument.AllowedFunctions</c> ile BIREBIR ayni oldugunu dogrular.
/// Uretim allow-list'ine yeni bir fonksiyon eklenip buraya karsiligi
/// eklenmezse test aninda kirmizyya doner — yani "sessizce cevrilmemis"
/// varsayilani "acik hata"ya cevrilir.
/// </para>
/// <para>
/// Kategoriler yerel <c>sqlite3 3.51</c> uzerinde olculerek belirlendi;
/// tahmine dayanmiyor.
/// </para>
/// </remarks>
internal static class TSqlFunctionSets
{
    /// <summary>
    /// SQLite'ta ayni adla ve ayni anlamda bulunan fonksiyonlar: hicbir islem
    /// gerekmez.
    /// </summary>
    /// <remarks>
    /// <c>CONCAT</c> SQLite 3.44'ten beri yerlesik; <c>CEILING</c> 3.35'ten beri
    /// <c>ceil</c> takma adiyla var. Bu yuzden fixture, motor surumunu dogrular.
    /// </remarks>
    public static IReadOnlySet<string> Native { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "SUM", "COUNT", "AVG", "MIN", "MAX",
        "NULLIF", "COALESCE",
        "ABS", "ROUND", "CEILING", "FLOOR",
        "UPPER", "LOWER", "LTRIM", "RTRIM", "TRIM", "CONCAT"
    };

    /// <summary>
    /// AST'de YERINDE yeniden adlandirilan fonksiyonlar: T-SQL adi -> SQLite adi.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Neden UDF degil de yeniden adlandirma:
    /// </para>
    /// <list type="bullet">
    ///   <item>
    ///     <c>ISNULL</c> SQLite'ta tokenizer ANAHTAR SOZCUGUDUR (<c>x ISNULL</c>
    ///     bicimindeki sonek operatoru). <c>ISNULL(a,b)</c> "no such function"
    ///     degil SOZDIZIMI HATASI verir, dolayisiyla <c>isnull</c> adinda bir UDF
    ///     tanimlamak imkansizdir. <c>COALESCE</c> ile anlam ayni: T-SQL
    ///     <c>ISNULL</c> ilk argumanin tipini dondurur, <c>COALESCE</c> tip
    ///     oncelisine gore secer — SQLite'ta statik tip olmadigi icin bu ayrim
    ///     ortadan kalkar.
    ///   </item>
    ///   <item>
    ///     <c>COUNT_BIG(*)</c> joker arguman kullanir; SQLite <c>f(*)</c> bicimini
    ///     yalnizca <c>count</c> icin kabul eder, bu yuzden
    ///     <c>CreateAggregate("count_big", ...)</c> ayristirilamaz. SQLite tam
    ///     sayilari 64 bit oldugu icin <c>COUNT</c> tam olarak <c>COUNT_BIG</c>'dir.
    ///   </item>
    /// </list>
    /// </remarks>
    public static IReadOnlyDictionary<string, string> Renamed { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ISNULL"] = "COALESCE",
            ["COUNT_BIG"] = "COUNT"
        };

    /// <summary>
    /// SQLite'ta bulunmayan ve C# tarafinda kullanici tanimli fonksiyon (UDF)
    /// olarak kaydedilen fonksiyonlar.
    /// </summary>
    /// <remarks>
    /// Anlamlari C#'ta ifade etmek, SQL'e cevirmekten daha guvenli. Ornek:
    /// T-SQL <c>DATEDIFF</c> SINIR GECISI sayar, gecen sureyi degil.
    /// <c>julianday(b) - julianday(a)</c> ifadesi
    /// <c>('2018-01-01 23:00', '2018-01-02 01:00')</c> icin 0 verir; T-SQL 1 der.
    /// C#'ta <c>(b.Date - a.Date).Days</c> bunu dogal olarak dogru yapar.
    /// </remarks>
    public static IReadOnlySet<string> UserDefined { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "YEAR", "MONTH", "DAY",
        "DATEPART", "DATEDIFF", "DATEADD", "EOMONTH",
        "LEN"
    };

    /// <summary>
    /// Ceviri sonrasi SQLite'in tanidigi fonksiyon adlari: yerlesikler artı
    /// kaydedilen UDF'ler. Yeniden adlandirilanlar bu asamada zaten hedef
    /// adlarina donusmus olur.
    /// </summary>
    public static IReadOnlySet<string> ExecutableAfterTranslation { get; } =
        new HashSet<string>(Native.Concat(UserDefined), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Harness'in bildigi tum T-SQL fonksiyon adlari (ceviri oncesi).
    /// </summary>
    public static IReadOnlySet<string> AllKnown { get; } =
        new HashSet<string>(
            Native.Concat(Renamed.Keys).Concat(UserDefined),
            StringComparer.OrdinalIgnoreCase);
}
