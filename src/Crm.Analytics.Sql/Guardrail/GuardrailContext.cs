using Crm.Analytics.Sql.Catalog;
using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.Parsing;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Crm.Analytics.Sql.Guardrail;

/// <summary>
/// Kontrollerin paylastigi durum. Kontroller sirayla calisir; okuma kontrolleri durumu
/// degistirmez, mutasyon kontrolleri (kapsam enjeksiyonu, parametreleme, satir siniri)
/// AST'yi ve uretilen SQL'i gunceller.
/// </summary>
/// <remarks>
/// Parametre adlari tek bir sayactan uretilir. Iki farkli mutasyonun ayni adi uretmesi,
/// birinin degerini digerinin uzerine yazmasi demek olurdu — bu, filtre degerinin sessizce
/// degismesi anlamina gelir.
/// </remarks>
public sealed class GuardrailContext
{
    private readonly List<SqlParameterSpec> parameters = [];
    private int parameterCounter;

    public GuardrailContext(
        string sql,
        AllowListDocument allowList,
        UserDataScope scope,
        TSqlParserFactory parserFactory,
        DataSource source = DataSource.Dwh,
        CanonicalRequest? request = null,
        IEnumerable<SqlParameterSpec>? initialParameters = null)
    {
        ArgumentNullException.ThrowIfNull(sql);
        ArgumentNullException.ThrowIfNull(allowList);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(parserFactory);

        // Query Builder kendi parametrelerini uretip SQL'e yerlestirir; guardrail bunlari
        // devralir. Aksi halde kabul edilen sonuc, SQL'de gecen parametrelerin bir kismini
        // TASIMAZDI ve sorgu calisma aninda "parametre eksik" hatasi verirdi.
        if (initialParameters is not null)
        {
            parameters.AddRange(initialParameters);
        }

        OriginalSql = sql;
        CurrentSql = sql;
        AllowList = allowList;
        Scope = scope;
        ParserFactory = parserFactory;
        Source = source;
        Request = request;
    }

    /// <summary>Guardrail'a gelen ham SQL. Audit icin korunur, degismez.</summary>
    public string OriginalSql { get; }

    /// <summary>Mutasyonlardan sonraki guncel SQL metni.</summary>
    public string CurrentSql { get; private set; }

    /// <summary>Guncel AST. <c>ParseToAst</c> kontrolunden once null.</summary>
    public TSqlFragment? Fragment { get; private set; }

    public AllowListDocument AllowList { get; }

    public UserDataScope Scope { get; }

    public TSqlParserFactory ParserFactory { get; }

    public DataSource Source { get; }

    /// <summary>
    /// Query Builder'in dogrulanmis canonical girdisi. Savunma katmanlari bu alan eksik olsa
    /// bile fail-closed davranir.
    /// </summary>
    public CanonicalRequest? Request { get; }

    public IReadOnlyList<SqlParameterSpec> Parameters => parameters;

    /// <summary>
    /// Uygulanan kapsam filtresinin okunabilir aciklamasi. Bos kalirsa sonuc Accepted
    /// olamaz — kapsamin gercekten uygulandiginin kanitidir.
    /// </summary>
    public string? AppliedScopeFilter { get; private set; }

    /// <summary>Kapsam filtresinin enjekte edildigi sorgu bloku sayisi (CTE ve UNION kollari dahil).</summary>
    public int ScopeFilterInjectionCount { get; private set; }

    /// <summary>
    /// AST'den okunup allow-list ile dogrulanan ilk fiziksel okuma objesi.
    /// Serbest kullanici metninden veya canonical alanlardan turetilmez.
    /// </summary>
    public string? VerifiedPhysicalObject { get; private set; }

    public void SetFragment(TSqlFragment fragment)
    {
        ArgumentNullException.ThrowIfNull(fragment);
        Fragment = fragment;
    }

    public TSqlFragment RequireFragment() =>
        Fragment ?? throw new InvalidOperationException(
            "AST henuz olusturulmadi. ParseToAst kontrolu calismadan agaca erisilemez.");

    /// <summary>
    /// AST degistirildi ancak SQL metni henuz yeniden uretilmedi.
    /// </summary>
    /// <remarks>
    /// Bu bayrak bir guvenlik kilididir: kapsam filtresi AST'ye eklenip SQL yeniden
    /// uretilmezse, <see cref="CurrentSql"/> hala <b>filtresiz</b> eski metni tasir. Pipeline,
    /// bekleyen degisiklik varken kabul sonucu uretmez.
    /// </remarks>
    public bool HasPendingAstChanges { get; private set; }

    /// <summary>AST'de degisiklik yapildigini isaretler.</summary>
    public void MarkAstMutated() => HasPendingAstChanges = true;

    public void ReplaceSql(string sql)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        CurrentSql = sql;
        HasPendingAstChanges = false;
    }

    /// <summary>Yeni ve tekil bir parametre adi uretir ('@' dahil).</summary>
    public string NextParameterName(string prefix = "p")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        return $"@{prefix}{parameterCounter++}";
    }

    public void AddParameter(SqlParameterSpec parameter)
    {
        ArgumentNullException.ThrowIfNull(parameter);

        if (parameters.Any(existing => existing.Name.Equals(parameter.Name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"'{parameter.Name}' parametresi zaten tanimli. Ayni adin iki kez uretilmesi, " +
                "bir degerin digerinin uzerine yazilmasi demektir.");
        }

        parameters.Add(parameter);
    }

    /// <summary>
    /// Kapsam filtresinin <b>gerekmedigi</b> durumu kaydeder: sinirsiz yetkili kullanici,
    /// kapsamdan muaf obje (tarih boyutu gibi) veya hic tablo okumayan sorgu.
    /// </summary>
    /// <remarks>
    /// Ayri bir metot olmasi bilinclidir: "filtre uygulandi" ile "filtre gerekmedi" audit'te
    /// karismamalidir. Her ikisi de <see cref="AppliedScopeFilter"/> doldurur, ancak
    /// <see cref="ScopeFilterInjectionCount"/> yalnizca gercek enjeksiyonda artar.
    /// </remarks>
    public void RecordScopeNotApplicable(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        AppliedScopeFilter = reason;
    }

    /// <summary>Kapsam filtresinin uygulandigini kaydeder.</summary>
    public void RecordScopeFilter(string description, int injectionCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        if (injectionCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(injectionCount), injectionCount,
                "Kapsam filtresi en az bir sorgu blokuna enjekte edilmis olmalidir.");
        }

        AppliedScopeFilter = description;
        ScopeFilterInjectionCount = injectionCount;
    }

    public void RecordVerifiedPhysicalObject(string physicalObject)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(physicalObject);
        VerifiedPhysicalObject = physicalObject;
    }
}
