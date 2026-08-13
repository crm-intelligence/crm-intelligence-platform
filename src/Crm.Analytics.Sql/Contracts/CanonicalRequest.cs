namespace Crm.Analytics.Sql.Contracts;

/// <summary>
/// Dogal dil talebinin yapilandirilmis hali. Deterministic Query Builder yalnizca bu nesneyi
/// gorur; ham kullanici metnini veya model tarafindan uretilmis SQL'i kabul etmez.
/// </summary>
/// <remarks>
/// <para>
/// Bu tip <b>kullanicinin veri kapsamini tasimaz.</b> Yetki bilgisi ayri bir kanaldan
/// (<c>UserDataScope</c>) gelir ve SQL'e guardrail tarafindan enjekte edilir. Kapsamin bu
/// sozlesmede olmamasi bilinclidir: modelin yetki karari vermesine yol acilmaz.
/// </para>
/// <para>
/// JSON sozlesmesi <c>canonical_request.schema.json</c> dosyasindadir ve Backend ile paylasilir.
/// </para>
/// </remarks>
public sealed record CanonicalRequest
{
    /// <summary>Tek bir talebin ucdan uca izlenmesini saglayan kimlik. Audit'in birincil anahtari.</summary>
    public required string RequestId { get; init; }

    /// <summary>Teams konusma baglami.</summary>
    public required string ConversationId { get; init; }

    /// <summary>
    /// Dolu ise bu talep bir <b>delta</b>'dir: yalnizca degisen alanlar uygulanir, geri kalan
    /// alanlar onceki talepten korunur. Takip sorusu akisinin temeli.
    /// </summary>
    public string? PreviousRequestId { get; init; }

    /// <summary>
    /// SQL uretiminden once kesinlestirilen source. Eski payload'larda null olabilir;
    /// null hicbir zaman DWH varsayimi anlamina gelmez.
    /// </summary>
    public DataSource? Source { get; init; }

    public required RequestIntent Intent { get; init; }

    /// <summary>
    /// Olculecek metrikler. Yalnizca Metric Catalog anahtarlari; serbest metin kabul edilmez.
    /// </summary>
    public required IReadOnlyList<string> Metrics { get; init; }

    /// <summary>
    /// Kirilim boyutlari. Yalnizca Metric Catalog'daki dimension anahtarlari.
    /// </summary>
    public IReadOnlyList<string> Dimensions { get; init; } = [];

    public IReadOnlyList<RequestFilter> Filters { get; init; } = [];

    public required DateRangeSpec DateRange { get; init; }

    public TimeGrain Grain { get; init; } = TimeGrain.None;

    /// <summary>
    /// Talebin istedigi satir siniri. Guardrail bunu allow-list'teki <c>maxRows</c> ile
    /// sinirlar; talebin daha yuksek bir deger istemesi limiti yukseltmez.
    /// </summary>
    public int? Limit { get; init; }

    /// <summary>ORDER BY icin yalnizca secili source katalogundaki dimension anahtari.</summary>
    public string? OrderBy { get; init; }

    public SortDirection OrderDirection { get; init; } = SortDirection.Asc;

    /// <summary>
    /// Eski payload uyumlulugu icin korunan Query Builder senaryo anahtari. Ollama planner
    /// bu alani her zaman null uretir; SQL yolu her durumda deterministiktir.
    /// </summary>
    public string? ScenarioKey { get; init; }

    /// <summary>Ayristirma guveni (0.0-1.0). Yalnizca advisory'dir; unresolved semantic kavramlari gecersiz kilamaz.</summary>
    public double Confidence { get; init; }

    /// <summary>
    /// Legacy deterministic parser uyumlulugu icin cozumlenemeyen terimler. Bos degilse
    /// <see cref="Confidence"/> degerinden bagimsiz olarak Query Builder yolu bloke edilir.
    /// </summary>
    public IReadOnlyList<string> UnresolvedTerms { get; init; } = [];
}
