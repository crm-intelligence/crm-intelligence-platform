using Crm.Analytics.Sql.Contracts;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Crm.Analytics.Sql.Guardrail.Checks;

/// <summary>
/// Kontrol 15: mutasyon sonrasi SQL'i yeniden uretir, yeniden parse eder ve okuma
/// kontrollerini <b>bastan</b> kosar. Pazarlik disi.
/// </summary>
/// <remarks>
/// <para>
/// Mutasyon yapan kontroller (kapsam enjeksiyonu, parametreleme, satir siniri) kendi acigini
/// yaratabilir: agaci degistirirler ve degisiklikten sonra hicbir kontrol tekrar kosmaz.
/// Bu kontrol o boslugu kapatir — yalnizca burada uretilen metin calistirilir.
/// </para>
/// <para>
/// Ayrica kapsam filtresinin uretilen metinde <b>gercekten</b> yer aldigi dogrulanir. AST'ye
/// filtre eklenmis olmasi yeterli degildir; uretici beklenmedik bir cikti verirse filtre
/// kaybolmus olabilir.
/// </para>
/// </remarks>
public sealed class RegenerateAndRevalidateCheck(IReadOnlyList<IGuardrailCheck> readOnlyChecks)
    : IGuardrailCheck
{
    public GuardrailCheckName Name => GuardrailCheckName.RegenerateAndRevalidate;

    public CheckResult Execute(GuardrailContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var regenerated = context.ParserFactory.GenerateScript(
            context.RequireFragment(), out var versioningErrors);

        if (versioningErrors.Count > 0)
        {
            // Uretilen AST hedef SQL surumu icin gecersiz. Cikti kullanilamaz.
            return CheckResult.Fail(Name, ReasonCode.GR014,
                $"Surum uyumsuzlugu: {versioningErrors[0].Message}");
        }

        var reparsed = context.ParserFactory.Parse(regenerated);

        if (!reparsed.IsSuccessful)
        {
            return CheckResult.Fail(Name, ReasonCode.GR014,
                $"Yeniden uretilen SQL parse edilemedi: {reparsed.ErrorSummary}");
        }

        context.ReplaceSql(regenerated);
        context.SetFragment(reparsed.RequireFragment());

        var scopeFailure = VerifyScopeParametersPresent(context, regenerated);

        if (scopeFailure is not null)
        {
            return scopeFailure;
        }

        // Okuma kontrolleri bastan kosar. Bir tanesi bile basarisiz olursa mutasyon
        // sorguyu bozmus veya bir aciga yol acmis demektir.
        foreach (var check in readOnlyChecks)
        {
            var result = check.Execute(context);

            if (result.Outcome != CheckOutcome.Passed)
            {
                return CheckResult.Fail(Name, ReasonCode.GR014,
                    $"Yeniden dogrulama basarisiz: {check.Name} -> {result.ReasonCode} ({result.Detail}).");
            }
        }

        return CheckResult.Pass(Name);
    }

    /// <summary>
    /// Kapsam parametrelerinin uretilen metinde yer aldigini AST uzerinden dogrular.
    /// </summary>
    private CheckResult? VerifyScopeParametersPresent(GuardrailContext context, string regenerated)
    {
        if (context.ScopeFilterInjectionCount == 0)
        {
            // Kapsam filtresi gerekmemis (sinirsiz yetki, muaf obje veya tablosuz sorgu).
            // Bu durumun mesruluguna ScopeFilterInjectionCheck karar verdi.
            return null;
        }

        var collector = new VariableReferenceCollector();
        context.RequireFragment().Accept(collector);

        var expected = context.Parameters
            .Select(parameter => parameter.Name)
            .Where(name => name.StartsWith("@scope", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        foreach (var parameterName in expected)
        {
            if (!collector.Names.Contains(parameterName))
            {
                return CheckResult.Fail(Name, ReasonCode.GR014,
                    $"Kapsam parametresi '{parameterName}' uretilen SQL'de bulunamadi.");
            }
        }

        return null;
    }

    private sealed class VariableReferenceCollector : TSqlFragmentVisitor
    {
        private readonly HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlySet<string> Names => names;

        public override void Visit(VariableReference node)
        {
            if (!string.IsNullOrWhiteSpace(node.Name))
            {
                names.Add(node.Name);
            }
        }
    }
}
