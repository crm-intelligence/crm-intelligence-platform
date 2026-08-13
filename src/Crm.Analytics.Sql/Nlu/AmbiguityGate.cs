using Crm.Analytics.Sql.Contracts;

namespace Crm.Analytics.Sql.Nlu;

/// <summary>
/// Talebin SQL uretimine girmeye yeterince net olup olmadigina karar verir.
/// </summary>
/// <remarks>
/// <para>
/// Canonical Request sozlesmesinin fail-closed kurali: <c>unresolvedTerms</c> bos degilse
/// <c>confidence</c> degerinden bagimsiz olarak sonuc netlestirmedir. Boylece yuksek model
/// confidence degeri, cozumlenemeyen bir business concept'i veya metric substitution'i
/// gecerli hale getiremez.
/// </para>
/// <para>
/// <b>Bu bir guvenlik kontrolu degildir</b> — guvenligi guardrail saglar. Buradaki risk
/// yetkisiz veri degil, <i>yanlis soruyu dogru gorunen bir raporla yanitlamaktir</i>. Yine de
/// kapi tek giris noktasina baglanir; atlanabilir bir kalite kontrolu zamanla atlanir.
/// </para>
/// <para>
/// Kapinin dil modeli tabanli ayristirici icin de gecerli olmasi kritik: model yuksek
/// <c>confidence</c> beyan edip alakasiz bir talep uretebilir. Bu yuzden metrik/boyut ve tarih
/// varligi da burada, modelin beyanindan bagimsiz olarak denetlenir.
/// </para>
/// </remarks>
public sealed class AmbiguityGate(double confidenceThreshold = AmbiguityGate.DefaultConfidenceThreshold)
{
    /// <summary>
    /// Varsayilan kapsama esigi. Talebin kelimelerinin en az %60'i bir anlama baglanmalidir.
    /// Deger konfigurasyona acik birakildi: dogru esik ancak gercek kullanici talepleriyle
    /// olculebilir, bu yuzden koda gomulu bir gercek gibi sunulmuyor.
    /// </summary>
    public const double DefaultConfidenceThreshold = 0.6;

    public double ConfidenceThreshold { get; } =
        confidenceThreshold is >= 0 and <= 1
            ? confidenceThreshold
            : throw new ArgumentOutOfRangeException(
                nameof(confidenceThreshold), confidenceThreshold, "Esik 0 ile 1 arasinda olmalidir.");

    /// <summary>
    /// Netlestirme gerekiyorsa gerekce kodunu, gerekmiyorsa <c>null</c> dondurur.
    /// </summary>
    public GateVerdict Evaluate(CanonicalRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Metrics.Count == 0 && request.Dimensions.Count == 0)
        {
            return new GateVerdict(ReasonCode.CL001, "Talep hicbir metrik veya boyut icermiyor.");
        }

        // Tarih araliginin ZORUNLU olup olmadigi burada denetlenmez: kural kaynaga baglidir
        // (vw_customer_rfm'de zaman boyutu yoktur, orada tarih istemek cikmaz sokak olurdu)
        // ve kaynagi bilen yer Query Builder'dir. Kapi yalnizca kendi icinde celisen araligi
        // yakalar.
        if (request.DateRange.From > request.DateRange.To)
        {
            return new GateVerdict(ReasonCode.CL002,
                "Tarih araliginin baslangici bitisinden sonra; aralik bos sonuc dondururdu.");
        }

        if (request.Metrics.Count == 0 && request.Intent != RequestIntent.List)
        {
            return new GateVerdict(ReasonCode.CL001,
                "Olcum isteyen talepte metric secilmedi.");
        }

        if (request.Metrics.Count > 0 && request.Dimensions.Count == 0
            && request.Intent is RequestIntent.Breakdown or RequestIntent.Compare)
        {
            return new GateVerdict(ReasonCode.CL001,
                "Kirilim veya karsilastirma isteyen talepte dimension secilmedi.");
        }

        // Confidence is advisory only. An unresolved business concept must not be hidden
        // by self-reported confidence or silently substituted with a supported metric.
        if (request.UnresolvedTerms.Count > 0)
        {
            return new GateVerdict(ReasonCode.CL001,
                $"Cozumlenemeyen terimler: {string.Join(", ", request.UnresolvedTerms)}. " +
                $"Kapsama {request.Confidence:0.00}, esik {ConfidenceThreshold:0.00}.");
        }

        return GateVerdict.Clear;
    }
}

/// <summary>
/// Belirsizlik kapisinin karari.
/// </summary>
/// <param name="ReasonCode">Netlestirme gerekcesi; <see cref="ReasonCode.None"/> ise talep gecebilir.</param>
/// <param name="Detail">Ic teshis notu; kullaniciya gosterilmez.</param>
public sealed record GateVerdict(ReasonCode ReasonCode, string? Detail)
{
    public static readonly GateVerdict Clear = new(ReasonCode.None, null);

    public bool NeedsClarification => ReasonCode != ReasonCode.None;
}
