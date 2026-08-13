namespace Crm.Analytics.Sql.Contracts;

/// <summary>Guardrail'in nihai karari.</summary>
public enum GuardrailDecision
{
    /// <summary>Sorgu calistirilabilir.</summary>
    Accepted,

    /// <summary>Guvenlik veya yetki kurali nedeniyle calistirilamaz.</summary>
    Rejected,

    /// <summary>Talep belirsiz; kullanicidan ek bilgi gerekiyor.</summary>
    NeedsClarification
}

/// <summary>Sorgunun hangi kaynaktan calistirilacagi (04-veri-stratejisi.md karar kurali).</summary>
public enum DataSource
{
    /// <summary>Varsayilan analitik kaynak.</summary>
    Dwh,

    /// <summary>Yalnizca guncellik gerektiren sinirli senaryolar; siki timeout ve satir limiti.</summary>
    Oltp
}

/// <summary>
/// Guardrail'in cikti sozlesmesi. Backend bunu alir; SQL'i yalnizca
/// <see cref="GuardrailDecision.Accepted"/> durumunda calistirir.
/// </summary>
/// <remarks>
/// <para>
/// Bilincli olarak <c>record</c> DEGIL <c>sealed class</c>: record'un <c>with</c> ifadesi
/// disaridan cagrilabildigi icin "Accepted ama SQL'i veya kapsam filtresi bos" gibi gecersiz
/// bir nesne uretilebilirdi. Nesne yalnizca fabrika metotlariyla kurulur ve invariant'lar
/// kurulum aninda zorlanir.
/// </para>
/// </remarks>
public sealed class GuardrailResult
{
    private GuardrailResult(
        GuardrailDecision decision,
        IReadOnlyList<CheckResult> checks,
        string? sql,
        IReadOnlyList<SqlParameterSpec> parameters,
        string? appliedScopeFilter,
        DataSource? source,
        int? queryTimeoutSeconds,
        int? rowLimit,
        string? physicalObject,
        ReasonCode reasonCode,
        string? parserVersion)
    {
        Decision = decision;
        Checks = checks;
        Sql = sql;
        Parameters = parameters;
        AppliedScopeFilter = appliedScopeFilter;
        Source = source;
        QueryTimeoutSeconds = queryTimeoutSeconds;
        RowLimit = rowLimit;
        PhysicalObject = physicalObject;
        ReasonCode = reasonCode;
        ParserVersion = parserVersion;
    }

    public GuardrailDecision Decision { get; }

    /// <summary>Calistirilmaya hazir SQL. Yalnizca <see cref="GuardrailDecision.Accepted"/> icin dolu.</summary>
    public string? Sql { get; }

    public IReadOnlyList<SqlParameterSpec> Parameters { get; }

    /// <summary>
    /// Guardrail'in enjekte ettigi kapsam filtresinin okunabilir hali. Audit'te "kapsam
    /// gercekten uygulandi mi" sorusunun kaniti; bos olamaz.
    /// </summary>
    public string? AppliedScopeFilter { get; }

    public DataSource? Source { get; }

    /// <summary>
    /// Komut timeout'u. Bu bir kontrol degil, sozlesme alanidir: uygulamasi execution
    /// katmanina aittir, guardrail yalnizca degeri belirler.
    /// </summary>
    public int? QueryTimeoutSeconds { get; }

    public int? RowLimit { get; }

    /// <summary>AST ve allow-list ile dogrulanmis birincil fiziksel okuma objesi.</summary>
    public string? PhysicalObject { get; }

    public ReasonCode ReasonCode { get; }

    /// <summary>Kullaniciya gosterilecek mesaj. Ic detay sizdirmaz.</summary>
    public string? ReasonMessage => ReasonCode == ReasonCode.None ? null : ReasonCodeMessages.For(ReasonCode);

    /// <summary>Kullanilan T-SQL parser surumu. Audit'te karari yeniden uretebilmek icin gerekli.</summary>
    public string? ParserVersion { get; }

    /// <summary>Kosulan, basarisiz olan ve atlanan tum kontroller.</summary>
    public IReadOnlyList<CheckResult> Checks { get; }

    public static GuardrailResult Accepted(
        string sql,
        IReadOnlyList<SqlParameterSpec> parameters,
        string appliedScopeFilter,
        DataSource source,
        int queryTimeoutSeconds,
        string parserVersion,
        IReadOnlyList<CheckResult> checks,
        int rowLimit = 5000,
        string? physicalObject = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(checks);

        // Kirmizi cizgi: kapsam filtresi uygulanmadan Accepted uretilemez. Bu kontrol
        // savunma amaclidir — pipeline'da bir hata olsa dahi kapsamsiz sorgu disa cikmaz.
        ArgumentException.ThrowIfNullOrWhiteSpace(appliedScopeFilter);

        if (queryTimeoutSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(queryTimeoutSeconds), queryTimeoutSeconds, "Timeout pozitif olmalidir.");
        }

        if (rowLimit <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rowLimit), rowLimit, "Satir limiti pozitif olmalidir.");
        }

        if (checks.Any(check => check.Outcome != CheckOutcome.Passed))
        {
            throw new InvalidOperationException(
                "Accepted sonucu, tum kontroller Passed olmadan uretilemez. " +
                $"Sorunlu kontroller: {string.Join(", ", checks.Where(c => c.Outcome != CheckOutcome.Passed).Select(c => c.Name))}");
        }

        return new GuardrailResult(
            GuardrailDecision.Accepted, checks, sql, parameters, appliedScopeFilter,
            source, queryTimeoutSeconds, rowLimit, physicalObject,
            ReasonCode.None, parserVersion);
    }

    public static GuardrailResult Rejected(
        ReasonCode reasonCode,
        IReadOnlyList<CheckResult> checks,
        string? parserVersion = null)
    {
        ArgumentNullException.ThrowIfNull(checks);

        if (reasonCode == ReasonCode.None)
        {
            throw new ArgumentException("Ret icin gerekce kodu zorunludur.", nameof(reasonCode));
        }

        // Reddedilen sorgunun SQL'i disa verilmez: kullaniciya veya loga sizmasi, reddedilen
        // ifadenin baska bir yoldan calistirilmasini kolaylastirir.
        return new GuardrailResult(
            GuardrailDecision.Rejected, checks, sql: null, parameters: [],
            appliedScopeFilter: null, source: null, queryTimeoutSeconds: null,
            rowLimit: null, physicalObject: null,
            reasonCode, parserVersion);
    }

    public static GuardrailResult NeedsClarification(
        ReasonCode reasonCode,
        IReadOnlyList<CheckResult> checks,
        string? parserVersion = null)
    {
        ArgumentNullException.ThrowIfNull(checks);

        if (reasonCode is not (ReasonCode.CL001 or ReasonCode.CL002))
        {
            throw new ArgumentException(
                "Netlestirme icin CL kodlarindan biri kullanilmalidir.", nameof(reasonCode));
        }

        return new GuardrailResult(
            GuardrailDecision.NeedsClarification, checks, sql: null, parameters: [],
            appliedScopeFilter: null, source: null, queryTimeoutSeconds: null,
            rowLimit: null, physicalObject: null,
            reasonCode, parserVersion);
    }
}
