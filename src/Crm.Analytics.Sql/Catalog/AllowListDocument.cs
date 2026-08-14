using System.Text.Json;
using System.Text.Json.Serialization;
using Crm.Analytics.Sql.Contracts;

namespace Crm.Analytics.Sql.Catalog;

/// <summary>
/// Izinli tablo, kolon ve JOIN yollarinin acik listesi. Guardrail'in "neyin var oldugu"
/// bilgisinin TEK kaynagi; veritabani semasina hicbir noktada sorulmaz.
/// </summary>
/// <remarks>
/// Bu dosya bir <b>konfigurasyon degil, guvenlik siniridir.</b> Yanlis yapilandirilmis bir
/// allow-list, tum kontrolleri gecen bir sizinti demektir; bu yuzden yukleme aninda
/// <see cref="AllowListLoader"/> tarafindan siki sekilde dogrulanir ve gecersizse
/// uygulama baslamaz.
/// </remarks>
public sealed class AllowListDocument
{
    /// <summary>
    /// Dosya icin serbest aciklama alani. Modelde yer almasi gereklidir: yukleyici bilinmeyen
    /// alanlari REDDEDER, cunku "deniedColumn" gibi bir yazim hatasinin sessizce bos listeye
    /// donusmesi dogrudan bir sizinti olurdu.
    /// </summary>
    [JsonPropertyName("_meta")]
    public JsonElement? Meta { get; init; }

    /// <summary>Obje adi (gorunum) -> izinli kolonlar ve JOIN yollari.</summary>
    public required IReadOnlyDictionary<string, AllowedObject> Objects { get; init; }

    /// <summary>
    /// Genel Query Builder yuzeyine acilmayan, yalnizca mevcut ve acikca
    /// adlandirilmis rapor/metric sozlesmelerinin calistirma yuzeyinde kullanabildigi
    /// fiziksel view'lar. Bu liste kolon katalogu degildir ve serbest sorgu kaynagi olmaz.
    /// </summary>
    public IReadOnlyDictionary<string, ScenarioObject> ScenarioObjects { get; init; } =
        new Dictionary<string, ScenarioObject>();

    /// <summary>
    /// Hicbir yerde kullanilamayacak kolonlar (PII). Yalnizca SELECT listesinde degil,
    /// WHERE / GROUP BY / ORDER BY / HAVING / JOIN ON dahil agacin her yerinde yasakli.
    /// </summary>
    public IReadOnlyList<string> DeniedColumns { get; init; } = [];

    /// <summary>
    /// Kimlik kolonlari. Bir sorgu bunlari kirilim (GROUP BY / SELECT) olarak kullanirsa
    /// tek kaydi izole edebilir; MinCellSize kontrolu bunu reddeder. Filtrede kullanilmasi
    /// yasak degildir, kirilimda kullanilmasi yasaktir.
    /// </summary>
    public IReadOnlyList<string> IdentityColumns { get; init; } = [];

    /// <summary>
    /// Cagrilmasina izin verilen fonksiyonlar. Listede olmayan her fonksiyon reddedilir.
    /// </summary>
    /// <remarks>
    /// Beyaz liste yaklasimi bilincli: kara liste tutmak, her yeni SQL surumunde yeni bir
    /// tehlikeli fonksiyonun sessizce gecmesi anlamina gelirdi. Varsayilan set, Metric
    /// Catalog'daki ifadelerin ihtiyac duydugu agregasyon ve donusum fonksiyonlariyla
    /// sinirlidir; sema/sunucu bilgisi sizdiran fonksiyonlar (DB_NAME, SUSER_NAME,
    /// OBJECT_NAME ...) bilincli olarak DISARIDA birakilmistir.
    /// </remarks>
    public IReadOnlyList<string> AllowedFunctions { get; init; } =
    [
        "SUM", "COUNT", "COUNT_BIG", "AVG", "MIN", "MAX",
        "NULLIF", "COALESCE", "ISNULL",
        "ABS", "ROUND", "CEILING", "FLOOR",
        "YEAR", "MONTH", "DAY", "DATEPART", "DATEDIFF", "DATEADD", "EOMONTH",
        "LEN", "UPPER", "LOWER", "LTRIM", "RTRIM", "TRIM", "CONCAT"
    ];

    public int MaxJoins { get; init; } = 2;

    public int MaxRows { get; init; } = 5000;

    /// <summary>Limitsiz list taleplerine uygulanacak guvenli varsayilan satir sayisi.</summary>
    public int DefaultRows { get; init; } = 5000;

    public int QueryTimeoutSeconds { get; init; } = 30;

    /// <summary>
    /// Tarih araligi ust siniri (gun). TOP donen satiri sinirlar, TARANAN satiri sinirlamaz;
    /// bu yuzden aralik ayrica butcelenir. Dokumandaki allow-list'te bulunmayan, eklenen alan.
    /// </summary>
    public int MaxDateRangeDays { get; init; } = 1100;

    /// <summary>Kabul edilecek en uzun SQL/talep metni. Kaynak tuketimine karsi ilk bariyer.</summary>
    public int MaxInputLength { get; init; } = 8000;

    /// <summary>
    /// Kirilim basina beklenen en az kayit sayisi. Kimlik seviyesinde detayi engellemek icin
    /// HAVING uzerinden tek kayit izole etme denemelerinde kullanilir.
    /// </summary>
    public int MinCellSize { get; init; } = 5;

    /// <summary>
    /// Kanitlanmis operational detail grain'inde identity kolonunun SELECT edilmesine izin verir.
    /// Varsayilan false; DWH aggregate yuzeyinde degistirilmez.
    /// </summary>
    public bool AllowIdentityDetail { get; init; }

    /// <summary>Obje allow-list'te mi (buyuk/kucuk harf duyarsiz).</summary>
    public bool HasObject(string objectName) => FindObject(objectName) is not null;

    public AllowedObject? FindObject(string objectName) =>
        Objects.FirstOrDefault(entry => entry.Key.Equals(
            objectName, StringComparison.OrdinalIgnoreCase)).Value;

    /// <summary>Logical catalog adini sabit, schema-qualified fiziksel ada cozer.</summary>
    public string? ResolvePhysicalObject(string logicalName)
    {
        var entry = Objects.FirstOrDefault(candidate => candidate.Key.Equals(
            logicalName, StringComparison.OrdinalIgnoreCase));
        return entry.Value is null
            ? null
            : entry.Value.PhysicalName ?? entry.Key;
    }

    /// <summary>
    /// Production SQL icindeki tam fiziksel ada ait genel sorgu politikasini bulur.
    /// Logical ad, fiziksel ad farkliysa burada bilincli olarak kabul edilmez.
    /// </summary>
    public AllowedObject? FindSqlObject(string physicalName) =>
        Objects.FirstOrDefault(entry => string.Equals(
            entry.Value.PhysicalName ?? entry.Key,
            physicalName,
            StringComparison.OrdinalIgnoreCase)).Value;

    public string? FindLogicalNameBySqlObject(string physicalName)
    {
        var entry = Objects.FirstOrDefault(candidate => string.Equals(
            candidate.Value.PhysicalName ?? candidate.Key,
            physicalName,
            StringComparison.OrdinalIgnoreCase));
        return entry.Value is null ? null : entry.Key;
    }

    public bool HasSqlObject(string physicalName) =>
        FindSqlObject(physicalName) is not null;

    /// <summary>
    /// Guardrail tarafindan dogrulanmis fiziksel adi contract'taki canonical
    /// yazimiyla dondurur. Kullanici metni bu degerin kaynagi olamaz.
    /// </summary>
    public string? ResolveCanonicalSqlObject(string physicalName)
    {
        var entry = Objects.FirstOrDefault(candidate => string.Equals(
            candidate.Value.PhysicalName ?? candidate.Key,
            physicalName,
            StringComparison.OrdinalIgnoreCase));
        return entry.Value is null
            ? null
            : entry.Value.PhysicalName ?? entry.Key;
    }

    public bool IsAllowedExecutionObject(string physicalName) =>
        HasSqlObject(physicalName)
        || ScenarioObjects.Keys.Any(name => name.Equals(
            physicalName, StringComparison.OrdinalIgnoreCase));

    /// <summary>Kolon yasakli PII listesinde mi.</summary>
    public bool IsDeniedColumn(string columnName) =>
        DeniedColumns.Contains(columnName, StringComparer.OrdinalIgnoreCase);

    /// <summary>Kolon kimlik kolonu mu (kirilimda kullanilmasi yasak).</summary>
    public bool IsIdentityColumn(string columnName) =>
        IdentityColumns.Contains(columnName, StringComparer.OrdinalIgnoreCase);

    /// <summary>Fonksiyon cagrisina izin verilmis mi.</summary>
    public bool IsAllowedFunction(string functionName) =>
        AllowedFunctions.Contains(functionName, StringComparer.OrdinalIgnoreCase);
}

/// <summary>Allow-list'teki tek bir gorunum.</summary>
public sealed class AllowedObject
{
    /// <summary>
    /// Logical catalog adinin production SQL'de kullanacagi sabit fiziksel ad. Null ise
    /// logical ad fiziksel ad olarak kullanilir (yalniz test/yerel sozlesme uyumlulugu).
    /// </summary>
    public string? PhysicalName { get; init; }

    public required IReadOnlyList<string> Columns { get; init; }

    /// <summary>
    /// Veri kapsami filtresinin uygulanacagi kolon. <see cref="IsScopeExempt"/> false ise
    /// zorunludur ve <see cref="Columns"/> icinde bulunmalidir.
    /// </summary>
    public string? ScopeColumn { get; init; }

    /// <summary>
    /// Bu obje kapsam filtresinden muaf mi (ornek: tarih boyut tablosu). <b>Acikca</b>
    /// isaretlenmesi gerekir; scopeColumn'un eksik olmasi sessiz muafiyet uretmez.
    /// </summary>
    [JsonPropertyName("scopeExempt")]
    public bool IsScopeExempt { get; init; }

    public IReadOnlyList<JoinPath> JoinPaths { get; init; } = [];

    /// <summary>
    /// TOP/list sorgularinda deterministik sonucu kanitlayan, sirali kolon kombinasyonu.
    /// Bos liste, bu obje icin guvenli bir tie-breaker kanitlanmadigi anlamina gelir.
    /// </summary>
    public IReadOnlyList<string> StableOrderColumns { get; init; } = [];

    public bool HasColumn(string columnName) =>
        Columns.Contains(columnName, StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Iki allow-listed logical obje arasindaki reviewed relationship contract'i.
/// Sol obje, bu tanimin yer aldigi <see cref="AllowedObject.JoinPaths"/> sahibidir.
/// Bu metadata fiziksel adlardan veya kolon benzerliginden turetilmez.
/// </summary>
public sealed record JoinPath
{
    /// <summary>Repository genelinde bu allow-list icinde benzersiz relationship anahtari.</summary>
    public required string Id { get; init; }

    /// <summary>Hedef logical obje adi.</summary>
    public required string To { get; init; }

    /// <summary>Sol logical objedeki allow-listed JOIN kolonu.</summary>
    public required string LeftColumn { get; init; }

    /// <summary>Sag logical objedeki allow-listed JOIN kolonu.</summary>
    public required string RightColumn { get; init; }

    public required RelationshipCardinality Cardinality { get; init; }

    public required DataSource LeftRuntime { get; init; }

    public required DataSource RightRuntime { get; init; }

    public required IReadOnlyList<ApprovedJoinType> AllowedJoinTypes { get; init; }

    /// <summary>Reviewed ancak gecici olarak devre disi bir relationship graph'a alinmaz.</summary>
    public bool Enabled { get; init; } = true;
}

public enum RelationshipCardinality
{
    OneToOne,
    OneToMany,
    ManyToOne,
    ManyToMany
}

public enum ApprovedJoinType
{
    Inner,
    Left
}

/// <summary>
/// Genel serbest sorgu kataloguna acilmayan fiziksel view ile onu mesru kilan mevcut
/// use-case/metric anahtarlari arasindaki versionlanmis bag.
/// </summary>
public sealed class ScenarioObject
{
    public required IReadOnlyList<string> Contracts { get; init; }
}
