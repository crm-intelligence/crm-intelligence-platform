using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.Guardrail;

namespace Crm.Analytics.Sql.Audit;

/// <summary>
/// Her SQL uretim kararini denetim kaydina yazar.
/// </summary>
/// <remarks>
/// Somut hedef (Application Insights, tablo, dosya) bu bilesenin karari degildir; log semasi
/// DevOps tarafindan standartlastirilir. Bu yuzden arayuz burada, uygulamasi cagiran tarafta.
/// </remarks>
public interface IDecisionAuditWriter
{
    void Write(DecisionAuditRecord record);
}

/// <summary>
/// Guardrail sonucundan denetim kaydi olusturur.
/// </summary>
public static class DecisionAuditRecordFactory
{
    public static DecisionAuditRecord From(
        GuardrailContext context,
        GuardrailResult result,
        ProductionPath path,
        string? userId = null,
        string? rawPrompt = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(result);

        return new DecisionAuditRecord
        {
            // requestId yoksa denetim izi kurulamaz; bu durumda acik bir yer tutucu yazilir
            // ki kaydin eksik oldugu gorunur olsun.
            RequestId = context.Request?.RequestId ?? "(bilinmiyor)",
            ConversationId = context.Request?.ConversationId ?? "(bilinmiyor)",
            PreviousRequestId = context.Request?.PreviousRequestId,
            EffectiveScope = DescribeScope(context.Scope),
            Path = path,
            Decision = result.Decision,
            ReasonCode = result.ReasonCode,
            Source = result.Source,
            ParserVersion = result.ParserVersion,
            AppliedScopeFilter = result.AppliedScopeFilter,
            // Yalnizca ADLAR. Degerler kullanici verisidir ve log'a yazilmaz.
            ParameterNames = result.Parameters.Select(parameter => parameter.Name).ToArray(),
            Checks = result.Checks
        };
    }

    /// <summary>
    /// Etkin kapsamin okunabilir ozeti. Dista da kullanilir: SQL uretimine hic girmeyen
    /// kararlarin (ornek: talep cozumlenemedi) denetim kaydinda da kapsam gorunmelidir.
    /// </summary>
    public static string DescribeScope(UserDataScope scope)
    {
        if (scope.IsUnrestricted)
        {
            return "SINIRSIZ";
        }

        if (!scope.IsResolvable)
        {
            return "COZUMLENEMEDI";
        }

        var parts = scope.Dimensions
            .Where(entry => entry.Value.Count > 0)
            .Select(entry => $"{entry.Key}:count={entry.Value.Count}");

        return string.Join("; ", parts);
    }
}
