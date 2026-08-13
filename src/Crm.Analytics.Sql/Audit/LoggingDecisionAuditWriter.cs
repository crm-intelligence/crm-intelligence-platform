using Crm.Analytics.Sql.Contracts;
using Microsoft.Extensions.Logging;

namespace Crm.Analytics.Sql.Audit;

/// <summary>
/// Denetim kaydini <see cref="ILogger"/> uzerinden yazar.
/// </summary>
/// <remarks>
/// <para>
/// Yapilandirilmis (structured) log kullanilir. Kullanici kimligi, ham istek, filtre degerleri
/// ve SQL bilincli olarak kayda dahil edilmez.
/// </para>
/// <para>
/// Seviye karara gore secilir. Reddedilen bir talep <see cref="LogLevel.Warning"/>, guardrail'in
/// KENDI hatasi (<see cref="ReasonCode.GR014"/>) ise <see cref="LogLevel.Error"/> ile yazilir —
/// ikincisi bir alarm konusudur, kullanici hatasi degildir.
/// </para>
/// </remarks>
public sealed class LoggingDecisionAuditWriter(ILogger<LoggingDecisionAuditWriter> logger)
    : IDecisionAuditWriter
{
    public void Write(DecisionAuditRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        var level = SelectLevel(record);

        logger.Log(
            level,
            "SQL karari: {Decision} | requestId={RequestId} conversationId={ConversationId} " +
            "previousRequestId={PreviousRequestId} scope={EffectiveScope} " +
            "path={Path} reason={ReasonCode} failedCheck={FailedCheck} " +
            "verifiedChecks={VerifiedCheckCount} source={Source} parser={ParserVersion} " +
            "scopeFilter={AppliedScopeFilter} parameters={ParameterNames}",
            record.Decision,
            record.RequestId,
            record.ConversationId,
            record.PreviousRequestId,
            record.EffectiveScope,
            record.Path,
            record.ReasonCode,
            record.FailedCheck,
            record.VerifiedCheckCount,
            record.Source,
            record.ParserVersion,
            record.AppliedScopeFilter,
            record.ParameterNames);
    }

    private static LogLevel SelectLevel(DecisionAuditRecord record) => record.ReasonCode switch
    {
        // Guardrail'in kendi hatasi: yeniden dogrulama basarisiz veya bir kontrol patladi.
        ReasonCode.GR014 => LogLevel.Error,

        // Yetkisiz erisim denemesi: guvenlik acisindan ayrica izlenmeli.
        ReasonCode.GR007 or ReasonCode.GR008 or ReasonCode.GR005 => LogLevel.Warning,

        _ => record.Decision switch
        {
            GuardrailDecision.Rejected => LogLevel.Warning,
            GuardrailDecision.NeedsClarification => LogLevel.Information,
            _ => LogLevel.Information
        }
    };
}
