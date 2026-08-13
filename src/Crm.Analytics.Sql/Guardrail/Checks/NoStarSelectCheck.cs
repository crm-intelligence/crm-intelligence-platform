using Crm.Analytics.Sql.Contracts;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Crm.Analytics.Sql.Guardrail.Checks;

/// <summary>
/// Kontrol 4: <c>SELECT *</c> kullanimini reddeder.
/// </summary>
/// <remarks>
/// Yildiz, calisma aninda kolonlara genisledigi icin allow-list kontrolunu etkisiz kilar:
/// bugun izinli kolonlara genisleyen bir <c>*</c>, gorunume yarin eklenen bir PII kolonunu
/// da sessizce dondurur. Nitelendirilmis bicim (<c>v.*</c>) de ayni sekilde reddedilir —
/// dokumanda yalnizca <c>SELECT *</c> yaziyordu, kapsam bilincli olarak genisletilmistir.
/// </remarks>
public sealed class NoStarSelectCheck : IGuardrailCheck
{
    public GuardrailCheckName Name => GuardrailCheckName.NoStarSelect;

    public CheckResult Execute(GuardrailContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var collector = new StarExpressionCollector();
        context.RequireFragment().Accept(collector);

        if (collector.Count > 0)
        {
            return CheckResult.Fail(Name, ReasonCode.GR004,
                $"{collector.Count} adet yildiz ifadesi bulundu. " +
                "Kolonlar acikca belirtilmelidir.");
        }

        return CheckResult.Pass(Name);
    }

    private sealed class StarExpressionCollector : TSqlFragmentVisitor
    {
        public int Count { get; private set; }

        // Hem 'SELECT *' hem 'SELECT v.*' bu dugum tipiyle temsil edilir;
        // nitelendirici Qualifier alaninda tasinir.
        public override void Visit(SelectStarExpression node) => Count++;
    }
}
