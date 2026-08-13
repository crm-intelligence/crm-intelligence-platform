using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.Guardrail.Checks;
using Crm.Analytics.Sql.Guardrail.Mutation;

namespace Crm.Analytics.Sql.Guardrail;

/// <summary>
/// Guardrail hattini kuran tek nokta.
/// </summary>
/// <remarks>
/// <para>
/// Uretimde SQL uretmek isteyen her kod yolu bu fabrikadan gecer. Kontrolleri elle
/// birlestiren ikinci bir yol birakilmaz — "guardrail'i bypass eden hicbir kod yolu
/// birakilmaz, test amacli bile" kirmizi cizgisi bunu gerektiriyor.
/// </para>
/// <para>
/// <b>Hat henuz tamamlanmadi.</b> Eksik kontroller <see cref="MissingChecks"/> ile acikca
/// raporlanir; sessizce eksik bir hat kurmak, eksigin unutulmasi anlamina gelirdi.
/// </para>
/// </remarks>
public static class GuardrailFactory
{
    /// <summary>
    /// Henuz yazilmamis kontroller. Hat tamamlandi: liste bos.
    /// </summary>
    public static IReadOnlyList<GuardrailCheckName> MissingChecks => [];

    /// <summary>
    /// Mutasyon sonrasi yeniden kosulabilen kontroller. <see cref="ParseToAstCheck"/> burada
    /// YER ALMAZ: yeniden parse islemi <see cref="RegenerateAndRevalidateCheck"/> tarafindan
    /// zaten yapilir. Mutasyon yapan kontroller de yer almaz; tekrar kosmalari ikinci kez
    /// filtre enjekte etmek olurdu.
    /// </summary>
    private static IReadOnlyList<IGuardrailCheck> CreateReadOnlyChecks() =>
    [
        new InputLimitsCheck(),
        new SingleStatementCheck(),
        new SelectOnlyCheck(),
        new NoStarSelectCheck(),
        new NodeTypeWhitelistCheck(),
        new AllowListObjectsCheck(),
        new NoDeniedPiiColumnsCheck(),
        new AllowListColumnsCheck(),
        new JoinPathAllowedCheck(),
        new MinCellSizeCheck()
    ];

    public static GuardrailPipeline Create() =>
        new([
            new InputLimitsCheck(),
            new ParseToAstCheck(),
            new SingleStatementCheck(),
            new SelectOnlyCheck(),
            new NoStarSelectCheck(),
            new NodeTypeWhitelistCheck(),
            new AllowListObjectsCheck(),
            new NoDeniedPiiColumnsCheck(),
            new AllowListColumnsCheck(),
            new JoinPathAllowedCheck(),
            new MinCellSizeCheck(),
            new DateRangeBudgetCheck(),
            new ScopeFilterInjectionCheck(new ScopeFilterInjector()),
            new LiteralParameterizationCheck(new LiteralParameterizer()),
            new RowLimitCheck(new RowLimitInjector()),
            new RegenerateAndRevalidateCheck(CreateReadOnlyChecks())
        ]);
}
