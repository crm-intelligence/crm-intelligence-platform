using Crm.Analytics.Sql.Audit;
using Crm.Analytics.Sql.Contracts;
using crm_project.Models;

namespace crm_project.Services;

/// <summary>
/// SQL uretim katmaninin karar kaydini uygulamanin denetim akisina baglar.
/// </summary>
/// <remarks>
/// <para>
/// Kutuphane denetim hedefini bilincli olarak cagirana birakiyor
/// (<see cref="IDecisionAuditWriter"/>). Bu adaptor iki yere yaziyor ve ikisi de gerekli:
/// </para>
/// <list type="number">
/// <item>
/// <see cref="LoggingDecisionAuditWriter"/> — kutuphanenin zengin yapilandirilmis kaydi
/// (hangi kontrolde takildi, kac kontrol gercekten dogrulandi, hangi yol kullanildi).
/// Bu alanlar <see cref="AuditLogEntry"/> semasina sigmiyor ve kaybedilmeleri
/// "guardrail neden reddetti" sorusunu cevaplanamaz hale getirirdi.
/// </item>
/// <item>
/// <see cref="AuditLogService"/> — uygulamanin tek satirlik <c>AUDIT | ...</c> formati.
/// <c>API.md</c>'deki KQL sorgulari bu formata bagli; SQL kararlarini bu akisin disinda
/// birakmak denetimi iki ayri yerden okumayi zorunlu kilardi.
/// </item>
/// </list>
/// </remarks>
public sealed class SqlDecisionAuditWriter(
    AuditLogService auditLogService,
    ILogger<LoggingDecisionAuditWriter> structuredLogger) : IDecisionAuditWriter
{
    private readonly LoggingDecisionAuditWriter _structured = new(structuredLogger);

    public void Write(DecisionAuditRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        _structured.Write(record);
        auditLogService.Write(ToAuditLogEntry(record));
    }

    private static AuditLogEntry ToAuditLogEntry(DecisionAuditRecord record) => new()
    {
        TimestampUtc = DateTime.UtcNow,
        RequestId = record.RequestId,
        // The semantic-planning audit intentionally excludes user identity.
        UserId = string.Empty,
        Decision = record.Decision.ToString(),
        Reason = DescribeReason(record),

        // Raw prompt and generated SQL are deliberately excluded from audit output.
        Query = string.Empty,

        // Kutuphane obje erisimini allow-list katalogundan yonetiyor; istemciden gelen bir
        // "hedef tablo" kavrami yok. Alan bu akista bos kalir.
        TargetTable = string.Empty,

        Source = record.Source?.ToString() ?? string.Empty,
        Region = record.EffectiveScope
    };

    /// <summary>
    /// Gerekce kodunu ve takilan kontrolu birlikte yazar: yalnizca kod, "hangi kontrol"
    /// sorusunu cevaplamiyor; yalnizca kontrol adi ise gerekce kodunu kaybediyor.
    /// </summary>
    private static string DescribeReason(DecisionAuditRecord record)
    {
        if (record.ReasonCode == ReasonCode.None)
        {
            return record.Decision == GuardrailDecision.Accepted
                ? $"Kabul edildi ({record.VerifiedCheckCount} kontrol dogrulandi)"
                : record.Decision.ToString();
        }

        return record.FailedCheck is null
            ? record.ReasonCode.ToString()
            : $"{record.ReasonCode} ({record.FailedCheck})";
    }
}
