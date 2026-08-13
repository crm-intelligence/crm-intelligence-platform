using Crm.Analytics.Sql.Contracts;

namespace Crm.Analytics.Sql.Guardrail.Checks;

/// <summary>
/// Kontrol 0: girdi boyutu siniri.
/// </summary>
/// <remarks>
/// Parse maliyetine girmeden once uygulanir. Cok uzun bir girdi parser'i gereksiz mesgul
/// eder; bu, ucuz bir kaynak tuketimi
/// vektorudur. Sinir allow-list'ten gelir, kodda sabit degildir.
/// </remarks>
public sealed class InputLimitsCheck : IGuardrailCheck
{
    public GuardrailCheckName Name => GuardrailCheckName.InputLimits;

    public CheckResult Execute(GuardrailContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var limit = context.AllowList.MaxInputLength;
        var length = context.CurrentSql.Length;

        if (length > limit)
        {
            return CheckResult.Fail(Name, ReasonCode.GR015,
                $"Girdi uzunlugu {length}, sinir {limit}.");
        }

        if (string.IsNullOrWhiteSpace(context.CurrentSql))
        {
            return CheckResult.Fail(Name, ReasonCode.GR015, "Girdi bos.");
        }

        return CheckResult.Pass(Name);
    }
}
