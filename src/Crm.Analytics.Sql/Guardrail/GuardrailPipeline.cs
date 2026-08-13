using Crm.Analytics.Sql.Contracts;

namespace Crm.Analytics.Sql.Guardrail;

/// <summary>
/// Kontrolleri sirayla calistirir ve ilk basarisiz kontrolde durur.
/// </summary>
/// <remarks>
/// <para>
/// <b>Fail-closed:</b> beklenmeyen bir istisna dahi kabul ile sonuclanmaz. Bir kontrol
/// patlarsa sonuc <see cref="ReasonCode.GR014"/> ile reddedilir; "kontrol calismadi" ile
/// "kontrol gecti" asla ayni sey degildir.
/// </para>
/// <para>
/// Atlanan kontroller <see cref="CheckOutcome.Skipped"/> olarak raporlanir. Audit'te hangi
/// kontrolun gercekten dogrulandigi gorunur olmali; atlanani "gecti" saymak, guvenlik
/// kanitini yaniltici hale getirirdi.
/// </para>
/// </remarks>
public sealed class GuardrailPipeline
{
    private readonly IReadOnlyList<IGuardrailCheck> checks;

    public GuardrailPipeline(IReadOnlyList<IGuardrailCheck> checks)
    {
        ArgumentNullException.ThrowIfNull(checks);

        if (checks.Count == 0)
        {
            throw new ArgumentException("Kontrol listesi bos olamaz.", nameof(checks));
        }

        ValidateOrder(checks);
        this.checks = checks;
    }

    /// <summary>Pipeline'da yer alan kontroller, calisma sirasiyla.</summary>
    public IReadOnlyList<GuardrailCheckName> CheckNames => checks.Select(check => check.Name).ToArray();

    public GuardrailResult Execute(GuardrailContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var results = new List<CheckResult>(checks.Count);

        for (var index = 0; index < checks.Count; index++)
        {
            var check = checks[index];
            var result = RunCheck(check, context);
            results.Add(result);

            if (result.Outcome == CheckOutcome.Passed)
            {
                continue;
            }

            // Ilk basarisizlikta dur; kalan kontroller kosulmadi olarak isaretlenir.
            for (var remaining = index + 1; remaining < checks.Count; remaining++)
            {
                results.Add(CheckResult.Skip(checks[remaining].Name));
            }

            return IsClarificationCode(result.ReasonCode)
                ? GuardrailResult.NeedsClarification(result.ReasonCode, results, context.ParserFactory.VersionName)
                : GuardrailResult.Rejected(result.ReasonCode, results, context.ParserFactory.VersionName);
        }

        return BuildAcceptedResult(context, results);
    }

    private static CheckResult RunCheck(IGuardrailCheck check, GuardrailContext context)
    {
        try
        {
            return check.Execute(context);
        }
        catch (Exception exception)
        {
            // Beklenmeyen hata kabule donusemez. Detay yalnizca audit icin tasinir.
            return CheckResult.Fail(
                check.Name,
                ReasonCode.GR014,
                $"Kontrol beklenmeyen sekilde basarisiz oldu: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private static GuardrailResult BuildAcceptedResult(GuardrailContext context, List<CheckResult> results)
    {
        // Savunma katmani: tum kontroller gectigi halde kapsam filtresi kaydedilmemisse
        // bu guardrail'in kendi hatasidir. Istisna firlatmak yerine reddediyoruz —
        // uretimde bir hata, filtresiz sorgunun disa cikmasina yol acmamali.
        if (string.IsNullOrWhiteSpace(context.AppliedScopeFilter))
        {
            results.Add(CheckResult.Fail(
                GuardrailCheckName.ScopeFilterInjection,
                ReasonCode.GR014,
                "Tum kontroller gecti ancak kapsam filtresi kaydedilmedi."));

            return GuardrailResult.Rejected(ReasonCode.GR014, results, context.ParserFactory.VersionName);
        }

        // AST degistirilip SQL yeniden uretilmediyse CurrentSql hala filtresiz eski metindir.
        // Bu durumda kabul uretmek, kapsam filtresinin hic uygulanmamasi demektir.
        if (context.HasPendingAstChanges)
        {
            results.Add(CheckResult.Fail(
                GuardrailCheckName.RegenerateAndRevalidate,
                ReasonCode.GR014,
                "AST degistirildi ancak SQL yeniden uretilmedi; uretilen metin mutasyonlari icermiyor."));

            return GuardrailResult.Rejected(ReasonCode.GR014, results, context.ParserFactory.VersionName);
        }

        return GuardrailResult.Accepted(
            context.CurrentSql,
            context.Parameters,
            context.AppliedScopeFilter,
            context.Source,
            context.AllowList.QueryTimeoutSeconds,
            context.ParserFactory.VersionName,
            results,
            context.AllowList.MaxRows,
            context.VerifiedPhysicalObject);
    }

    private static bool IsClarificationCode(ReasonCode code) =>
        code is ReasonCode.CL001 or ReasonCode.CL002;

    private static void ValidateOrder(IReadOnlyList<IGuardrailCheck> checks)
    {
        // Sira bir uygulama detayi degil, guvenlik gereksinimidir: ornegin kapsam
        // enjeksiyonu, allow-list dogrulamasindan ONCE calisirsa izinsiz bir objeye filtre
        // eklenmis olur ve kontrol sirasi anlamsizlasir.
        for (var index = 1; index < checks.Count; index++)
        {
            var previous = checks[index - 1].Name;
            var current = checks[index].Name;

            if (current == previous)
            {
                throw new ArgumentException(
                    $"'{current}' kontrolu birden fazla kez tanimlanmis.", nameof(checks));
            }

            if (current < previous)
            {
                throw new ArgumentException(
                    $"Kontroller GuardrailCheckName sirasina gore verilmelidir. " +
                    $"'{current}', '{previous}' kontrolunden sonra geliyor.", nameof(checks));
            }
        }
    }
}
