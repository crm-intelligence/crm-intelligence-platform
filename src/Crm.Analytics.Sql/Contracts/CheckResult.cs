namespace Crm.Analytics.Sql.Contracts;

/// <summary>Tek bir kontrolun sonucu.</summary>
public enum CheckOutcome
{
    /// <summary>Kontrol gecti.</summary>
    Passed,

    /// <summary>Kontrol basarisiz; pipeline burada durdu.</summary>
    Failed,

    /// <summary>
    /// Kontrol kosulmadi. Onceki bir kontrol basarisiz oldugu icin atlandi. Atlanan kontrolun
    /// "gecti" gibi raporlanmamasi onemli: audit'te hangi kontrolun gercekten dogrulandigi
    /// gorunur olmali.
    /// </summary>
    Skipped
}

/// <summary>
/// Bir guardrail kontrolunun sonucu ve gerekcesi.
/// </summary>
/// <param name="Name">Kontrol adi.</param>
/// <param name="Outcome">Sonuc.</param>
/// <param name="ReasonCode">Basarisizlik gerekce kodu; <see cref="CheckOutcome.Passed"/> icin None.</param>
/// <param name="Detail">
/// Ic teshis notu. <b>Kullaniciya gosterilmez</b> — yalnizca audit ve gelistirme icin.
/// Kullaniciya gosterilse sema kesfi icin oracle olusurdu.
/// </param>
public sealed record CheckResult(
    GuardrailCheckName Name,
    CheckOutcome Outcome,
    ReasonCode ReasonCode = ReasonCode.None,
    string? Detail = null)
{
    public static CheckResult Pass(GuardrailCheckName name) => new(name, CheckOutcome.Passed);

    public static CheckResult Fail(GuardrailCheckName name, ReasonCode reasonCode, string? detail = null) =>
        new(name, CheckOutcome.Failed, reasonCode, detail);

    public static CheckResult Skip(GuardrailCheckName name) => new(name, CheckOutcome.Skipped);
}
