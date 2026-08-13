using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.Guardrail.Mutation;

namespace Crm.Analytics.Sql.Guardrail.Checks;

/// <summary>
/// Kontrol 12: kullanicinin veri kapsami filtresini sorguya <b>zorla</b> ekler.
/// </summary>
/// <remarks>
/// Bu kontrol "filtre var mi?" diye bakmaz, filtreyi kendisi ekler. Kullanicinin veya dil
/// modelinin yazdigi bir kapsam kosuluna guvenilmez.
/// </remarks>
public sealed class ScopeFilterInjectionCheck(ScopeFilterInjector injector) : IGuardrailCheck
{
    public GuardrailCheckName Name => GuardrailCheckName.ScopeFilterInjection;

    public CheckResult Execute(GuardrailContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Fail-closed: yetkisi cozumlenemeyen kullanici hicbir sey goremez. Bos kapsamin
        // "kisitlama yok" olarak yorumlanmasi en tehlikeli hata olurdu.
        if (!context.Scope.IsResolvable)
        {
            return CheckResult.Fail(Name, ReasonCode.GR007,
                "Kullanicinin veri kapsami cozumlenemedi.");
        }

        if (context.Scope.IsUnrestricted)
        {
            context.RecordScopeNotApplicable(
                "SINIRSIZ: kullanici tum veri kapsamina yetkili, filtre eklenmedi.");
            return CheckResult.Pass(Name);
        }

        var boundaryFailure = CheckRequestedScopeBoundary(context);

        if (boundaryFailure is not null)
        {
            return boundaryFailure;
        }

        var outcome = injector.Inject(context);

        if (!outcome.AnythingInjected)
        {
            // Filtre eklenmemesinin mesru iki sebebi var: tum objeler kapsamdan muaf
            // (tarih boyutu gibi) veya sorgu hic tablo okumuyor. Her ikisi de audit'te
            // ayrica gorunur; "filtre uygulandi" ile karismaz.
            context.RecordScopeNotApplicable(outcome.ExemptTableCount > 0
                ? $"MUAF: {outcome.ExemptTableCount} obje kapsamdan muaf, filtre gerekmedi."
                : "TABLO YOK: sorgu kapsam filtresi gerektiren bir obje okumuyor.");

            return CheckResult.Pass(Name);
        }

        context.RecordScopeFilter(
            string.Join(" AND ", outcome.Description),
            outcome.InjectedPredicateCount);

        // AST degisti; SQL'in yeniden uretilmesi RegenerateAndRevalidate kontrolunun isidir.
        // Bayrak, o kontrol calismazsa kabul uretilmesini engeller.
        context.MarkAstMutated();

        return CheckResult.Pass(Name);
    }

    /// <summary>
    /// Kullanicinin acikca yetkisiz bir kapsam degeri talep edip etmedigini denetler
    /// (<see cref="ReasonCode.GR008"/>).
    /// </summary>
    /// <remarks>
    /// Bu kontrol veri sizintisini onlemek icin DEGIL, dogru cevabi vermek ve denetim izi
    /// birakmak icin var: enjekte edilen filtre zaten sizintiyi engelliyor, ancak sonuc
    /// kullaniciya "veri bulunamadi" gibi gorunuyordu. Yetkisiz erisim denemesi audit'te
    /// kendi koduyla gorunmeli.
    /// </remarks>
    private CheckResult? CheckRequestedScopeBoundary(GuardrailContext context)
    {
        var allowedValues = context.Scope.ValuesFor(UserDataScope.RegionDimension);

        if (allowedValues.Count == 0)
        {
            return null;
        }

        var scopeColumns = context.AllowList.Objects.Values
            .Where(allowed => !allowed.IsScopeExempt && allowed.ScopeColumn is not null)
            .Select(allowed => allowed.ScopeColumn!)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var scopeColumn in scopeColumns)
        {
            var requested = Visitors.ScopeValueReader.ReadFrom(context.RequireFragment(), scopeColumn);

            var outside = requested
                .Where(value => !allowedValues.Contains(value, StringComparer.OrdinalIgnoreCase))
                .ToArray();

            if (outside.Length > 0)
            {
                return CheckResult.Fail(Name, ReasonCode.GR008,
                    $"Kapsam disi deger talep edildi: {string.Join(", ", outside)}.");
            }
        }

        return null;
    }
}
