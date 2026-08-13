using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.Guardrail.Mutation;

namespace Crm.Analytics.Sql.Guardrail.Checks;

/// <summary>
/// Kontrol 13: filtre literal'lerini parametreye cevirir.
/// </summary>
/// <remarks>
/// Bu kontrol savunma derinligi icin literal'i reddetmez, DONUSTURUR. Donusum sonrasi kullanici degeri
/// SQL metninde yer almaz — "hicbir yerde string birlestirmeyle SQL uretilmez" kirmizi
/// cizgisinin karsiligi budur.
/// </remarks>
public sealed class LiteralParameterizationCheck(LiteralParameterizer parameterizer) : IGuardrailCheck
{
    public GuardrailCheckName Name => GuardrailCheckName.LiteralParameterization;

    public CheckResult Execute(GuardrailContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var outcome = parameterizer.Parameterize(context);

        if (outcome.ParameterizedCount > 0)
        {
            context.MarkAstMutated();
        }

        return CheckResult.Pass(Name);
    }
}
