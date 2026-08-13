using Crm.Analytics.Sql.Contracts;

namespace Crm.Analytics.Sql.Audit;

/// <summary>SQL'in hangi yoldan uretildigi.</summary>
public enum ProductionPath
{
    /// <summary>Deterministik Query Builder.</summary>
    QueryBuilder
}

/// <summary>
/// Tek bir SQL uretim kararinin denetim kaydi.
/// </summary>
/// <remarks>
/// <para>
/// <b>Parametre DEGERLERI kaydedilmez.</b> Yalnizca parametre adlari ve sayisi tutulur.
/// Filtre degerleri kullanici verisidir (sehir, kategori, tarih ve ileride kisisel olabilecek
/// alanlar); bunlari log'a yazmak, guardrail'in PII kontrollerini bir yandan uygularken
/// ayni veriyi diger yandan log altyapisina sizdirmak olurdu.
/// </para>
/// <para>
/// SQL, ham prompt ve kullanici kimligi bu audit contract'inda hic yer almaz.
/// </para>
/// </remarks>
public sealed record DecisionAuditRecord
{
    public required string RequestId { get; init; }

    public required string ConversationId { get; init; }

    /// <summary>Dolu ise bu talep bir revizyondur (takip sorusu).</summary>
    public string? PreviousRequestId { get; init; }

    /// <summary>
    /// Etkin veri kapsaminin degersiz ozeti (ornek: "region:count=2" veya "SINIRSIZ").
    /// </summary>
    public required string EffectiveScope { get; init; }

    public required ProductionPath Path { get; init; }

    public required GuardrailDecision Decision { get; init; }

    public required ReasonCode ReasonCode { get; init; }

    public DataSource? Source { get; init; }

    /// <summary>
    /// Kullanilan T-SQL parser surumu. Karari sonradan yeniden uretebilmek icin gerekli:
    /// parser surumu degistiginde ayni SQL farkli degerlendirilebilir.
    /// </summary>
    public string? ParserVersion { get; init; }

    /// <summary>Uygulanan kapsam filtresinin okunabilir hali veya muafiyet gerekcesi.</summary>
    public string? AppliedScopeFilter { get; init; }

    /// <summary>Parametre adlari. DEGERLER KAYDEDILMEZ.</summary>
    public IReadOnlyList<string> ParameterNames { get; init; } = [];

    /// <summary>Kosulan, basarisiz olan ve atlanan tum kontroller.</summary>
    public IReadOnlyList<CheckResult> Checks { get; init; } = [];

    /// <summary>Basarisiz olan kontrolun adi. Ret nedenini tek bakista gosterir.</summary>
    public GuardrailCheckName? FailedCheck =>
        Checks.FirstOrDefault(check => check.Outcome == CheckOutcome.Failed)?.Name;

    /// <summary>Gercekten dogrulanan kontrol sayisi. Atlananlar sayilmaz.</summary>
    public int VerifiedCheckCount =>
        Checks.Count(check => check.Outcome == CheckOutcome.Passed);
}
