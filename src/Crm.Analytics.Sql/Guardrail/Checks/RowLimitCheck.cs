using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.Guardrail.Mutation;

namespace Crm.Analytics.Sql.Guardrail.Checks;

/// <summary>
/// Kontrol 14: satir sinirini uygular.
/// </summary>
/// <remarks>
/// Kontrol degil MUTASYON niteligindedir: eksik limiti reddetmek yerine ekler. Talebin daha
/// yuksek bir limit istemesi siniri yukseltmez.
/// </remarks>
public sealed class RowLimitCheck(RowLimitInjector injector) : IGuardrailCheck
{
    public GuardrailCheckName Name => GuardrailCheckName.RowLimit;

    public CheckResult Execute(GuardrailContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var outcome = injector.Apply(context);

        if (outcome.InjectedCount + outcome.LoweredCount > 0)
        {
            context.MarkAstMutated();
        }

        // Disa cikan tek bir blok bile limitsiz kalmamali. Bu durumun olusmasi guardrail'in
        // kendi hatasidir; sessizce gecmek limitsiz sorgu uretmek olurdu.
        if (outcome.InjectedCount == 0 && outcome.LoweredCount == 0)
        {
            var hasLimitAlready = HasLimitOnEveryOutermostBlock(context);

            if (!hasLimitAlready)
            {
                return CheckResult.Fail(Name, ReasonCode.GR009,
                    "Disa cikan sorgu bloguna satir siniri uygulanamadi.");
            }
        }

        return CheckResult.Pass(Name);
    }

    private static bool HasLimitOnEveryOutermostBlock(GuardrailContext context) =>
        Visitors.QueryStructure.OutermostBlocks(context.RequireFragment())
            .All(block => block.TopRowFilter is not null);
}
