using Crm.Analytics.Sql.Contracts;

namespace Crm.Analytics.Sql.Nlu;

/// <summary>
/// Ayristiriciya verilen girdi.
/// </summary>
/// <param name="Prompt">Kullanicinin serbest metni.</param>
/// <param name="RequestId">Bu talebe atanan kimlik. Audit kaydi buna baglanir.</param>
/// <param name="ConversationId">Konusma kimligi.</param>
/// <param name="Today">
/// Gorece tarih ifadelerinin cozumlendigi referans gun. <b>Zorunlu ve dista</b>: ayristirici
/// sistem saatini okumaz, boylece ayni girdi ayni referans gunle her zaman ayni sonucu verir
/// ve audit kaydi yeniden uretilebilir olur.
/// </param>
/// <param name="PreviousRequestId">Takip sorusuysa revize edilecek talebin kimligi.</param>
public sealed record RequestParseInput(
    string Prompt,
    string RequestId,
    string ConversationId,
    DateOnly Today,
    string? PreviousRequestId = null);

/// <summary>
/// Serbest metni Canonical Request'e cevirir.
/// </summary>
/// <remarks>
/// <para>
/// Arayuz olarak ayrilmasi bilinclidir: bugun katalog terimlerine dayanan deterministik bir
/// uygulama var (<see cref="CatalogTermRequestParser"/>), yarin dil modeli tabanli bir
/// uygulama gelebilir. <b>Iki durumda da cikti ayni sozlesmedir</b> ve ayni belirsizlik
/// kapisindan (<see cref="AmbiguityGate"/>) gecer — model ciktisina guvenilerek hicbir adim
/// atlanmaz.
/// </para>
/// <para>
/// Ayristirici <b>SQL uretmez</b>. Yalnizca talebi katalog anahtarlarina baglar; SQL uretimi
/// Query Builder'in, calistirma onayi guardrail'in isidir.
/// </para>
/// </remarks>
public interface IRequestParser
{
    /// <summary>Yeni talebi cozumler. Metrik/boyut ve gerekiyorsa tarih araligi zorunludur.</summary>
    RequestParseOutcome Parse(RequestParseInput input);

    /// <summary>
    /// Takip sorusunu delta olarak cozumler. Belirtilmeyen alanlar "degismedi" sayilir.
    /// </summary>
    RequestDeltaOutcome ParseRevision(RequestParseInput input);
}

/// <summary>
/// Takip sorusunun cozumleme sonucu.
/// </summary>
public sealed class RequestDeltaOutcome
{
    private RequestDeltaOutcome(
        CanonicalRequestDelta? delta,
        ReasonCode reasonCode,
        string? detail,
        IReadOnlyList<string> unresolvedTerms,
        double confidence)
    {
        Delta = delta;
        ReasonCode = reasonCode;
        Detail = detail;
        UnresolvedTerms = unresolvedTerms;
        Confidence = confidence;
    }

    public bool IsSuccessful => Delta is not null;

    public CanonicalRequestDelta? Delta { get; }

    public ReasonCode ReasonCode { get; }

    public string? Detail { get; }

    public IReadOnlyList<string> UnresolvedTerms { get; }

    /// <summary>
    /// Revizyon metninin kapsama orani. Revize edilmis talebe <b>bu deger</b> yazilir;
    /// oncekinin skorunu korumak, belirsiz bir takip sorusunun belirsizlik kapisini
    /// gecmesine yol acardi.
    /// </summary>
    public double Confidence { get; }

    public static RequestDeltaOutcome Success(
        CanonicalRequestDelta delta,
        IReadOnlyList<string> unresolvedTerms,
        double confidence)
    {
        ArgumentNullException.ThrowIfNull(delta);

        return new RequestDeltaOutcome(delta, ReasonCode.None, null, unresolvedTerms, confidence);
    }

    public static RequestDeltaOutcome Failure(
        ReasonCode reasonCode,
        string detail,
        IReadOnlyList<string>? unresolvedTerms = null,
        double confidence = 0)
    {
        if (reasonCode == ReasonCode.None)
        {
            throw new ArgumentException("Basarisizlik icin gerekce kodu zorunludur.", nameof(reasonCode));
        }

        return new RequestDeltaOutcome(null, reasonCode, detail, unresolvedTerms ?? [], confidence);
    }
}

/// <summary>
/// Ayristirma sonucu.
/// </summary>
public sealed class RequestParseOutcome
{
    private RequestParseOutcome(
        CanonicalRequest? request,
        ReasonCode reasonCode,
        string? detail,
        IReadOnlyList<string> unresolvedTerms)
    {
        Request = request;
        ReasonCode = reasonCode;
        Detail = detail;
        UnresolvedTerms = unresolvedTerms;
    }

    public bool IsSuccessful => Request is not null;

    public CanonicalRequest? Request { get; }

    public ReasonCode ReasonCode { get; }

    /// <summary>Ic teshis notu; kullaniciya gosterilmez.</summary>
    public string? Detail { get; }

    /// <summary>
    /// Katalogda karsiligi bulunamayan terimler. Basarili sonucta da dolu olabilir:
    /// talebin anlasilan kismi yeterliyse cozumlenemeyen bir kelime tek basina reddi
    /// gerektirmez, karar <see cref="AmbiguityGate"/>'e aittir.
    /// </summary>
    public IReadOnlyList<string> UnresolvedTerms { get; }

    public static RequestParseOutcome Success(CanonicalRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new RequestParseOutcome(request, ReasonCode.None, null, request.UnresolvedTerms);
    }

    public static RequestParseOutcome Failure(
        ReasonCode reasonCode,
        string detail,
        IReadOnlyList<string>? unresolvedTerms = null)
    {
        if (reasonCode == ReasonCode.None)
        {
            throw new ArgumentException("Basarisizlik icin gerekce kodu zorunludur.", nameof(reasonCode));
        }

        return new RequestParseOutcome(null, reasonCode, detail, unresolvedTerms ?? []);
    }
}
