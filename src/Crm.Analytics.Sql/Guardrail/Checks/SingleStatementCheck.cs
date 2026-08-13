using Crm.Analytics.Sql.Contracts;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Crm.Analytics.Sql.Guardrail.Checks;

/// <summary>
/// Kontrol 2: tek bir ifade olmasini zorunlu kilar.
/// </summary>
/// <remarks>
/// Sayim <b>AST uzerinden</b> yapilir, metinde ';' aranmaz. Metin tabanli sayim iki yonlu
/// hata uretir: <c>WHERE note = 'a;b'</c> yanlis pozitif, yorum icine saklanmis bir zincir
/// ise yanlis negatif olurdu.
/// </remarks>
public sealed class SingleStatementCheck : IGuardrailCheck
{
    public GuardrailCheckName Name => GuardrailCheckName.SingleStatement;

    public CheckResult Execute(GuardrailContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var fragment = context.RequireFragment();

        if (fragment is not TSqlScript script)
        {
            // Beklenmeyen kok tipi: fail-closed.
            return CheckResult.Fail(Name, ReasonCode.GR002,
                $"Beklenmeyen kok dugum tipi: {fragment.GetType().Name}");
        }

        var statementCount = script.Batches.Sum(batch => batch.Statements.Count);

        if (statementCount != 1)
        {
            return CheckResult.Fail(Name, ReasonCode.GR002,
                $"Tek ifade beklenirken {statementCount} ifade bulundu.");
        }

        if (script.Batches.Count != 1)
        {
            // GO ile ayrilmis coklu batch.
            return CheckResult.Fail(Name, ReasonCode.GR002,
                $"Tek batch beklenirken {script.Batches.Count} batch bulundu.");
        }

        return CheckResult.Pass(Name);
    }
}
