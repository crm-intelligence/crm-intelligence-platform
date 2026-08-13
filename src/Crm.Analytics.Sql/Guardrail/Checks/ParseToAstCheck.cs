using Crm.Analytics.Sql.Contracts;

namespace Crm.Analytics.Sql.Guardrail.Checks;

/// <summary>
/// Kontrol 1: sorguyu gercek T-SQL parser ile AST'ye cevirir.
/// </summary>
/// <remarks>
/// Regex ile dogrulama yapilmaz — bu bir kirmizi cizgidir. Parser fail-closed davranir:
/// anlamadigi sozdizimi icin hata uretir, sessizce "en iyi tahmin" uretmez.
/// Hata listesi bos degilse fragment dolu olsa dahi sonuc basarisizdir; kismi parse edilmis
/// bir agac uzerinde guvenlik kontrolu yapmak yanilticidir.
/// </remarks>
public sealed class ParseToAstCheck : IGuardrailCheck
{
    public GuardrailCheckName Name => GuardrailCheckName.ParseToAst;

    public CheckResult Execute(GuardrailContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var parsed = context.ParserFactory.Parse(context.CurrentSql);

        if (!parsed.IsSuccessful)
        {
            return CheckResult.Fail(Name, ReasonCode.GR002,
                $"Parse edilemedi: {parsed.ErrorSummary}");
        }

        context.SetFragment(parsed.RequireFragment());
        return CheckResult.Pass(Name);
    }
}
