namespace Crm.Analytics.Sql.Contracts;

/// <summary>
/// Onceki talebe uygulanacak degisiklik (takip sorusu / rapor revizyonu).
/// </summary>
/// <remarks>
/// <para>
/// Her alan <b>nullable</b>: <c>null</c> "degismedi" anlamina gelir. Bu ayrim zorunludur —
/// <see cref="CanonicalRequest"/> uzerinde bos liste ile "belirtilmemis" birbirinden
/// ayirt edilemezdi ve "bolge yerine magaza bazinda goster" talebi butun metrikleri silmis
/// gibi yorumlanabilirdi.
/// </para>
/// <para>
/// Filtreler icin ekleme ve cikarma ayri alanlardir: kullanici genellikle "kampanyalilari da
/// ekle" veya "kategori filtresini kaldir" der, filtre listesinin tamamini yeniden saymaz.
/// </para>
/// </remarks>
public sealed record CanonicalRequestDelta
{
    public DataSource? Source { get; init; }

    public RequestIntent? Intent { get; init; }

    /// <summary>Metriklerin tamami degistirilir. "Satis adedi yerine net kar" talebi bunu kullanir.</summary>
    public IReadOnlyList<string>? Metrics { get; init; }

    /// <summary>Boyutlarin tamami degistirilir. "Bolge yerine magaza bazinda" talebi bunu kullanir.</summary>
    public IReadOnlyList<string>? Dimensions { get; init; }

    /// <summary>Eklenecek filtreler. Ayni alana ait onceki filtre DEGISTIRILIR, cift filtre olusmaz.</summary>
    public IReadOnlyList<RequestFilter>? FiltersToAdd { get; init; }

    /// <summary>Kaldirilacak filtrelerin alan adlari.</summary>
    public IReadOnlyList<string>? FilterFieldsToRemove { get; init; }

    public DateRangeSpec? DateRange { get; init; }

    public TimeGrain? Grain { get; init; }

    public int? Limit { get; init; }

    public string? OrderBy { get; init; }

    public SortDirection? OrderDirection { get; init; }
}

/// <summary>
/// Onceki talebi revize eder.
/// </summary>
public static class CanonicalRequestReviser
{
    /// <summary>
    /// <paramref name="delta"/>'yi <paramref name="previous"/> uzerine uygular ve yeni bir
    /// talep dondurur.
    /// </summary>
    /// <remarks>
    /// Yeni talep, kendi <paramref name="newRequestId"/>'sini alir ve
    /// <see cref="CanonicalRequest.PreviousRequestId"/> alaninda oncekine baglanir. Zincir
    /// korunur: denetimde "bu rapor hangi talebin revizyonu" sorusu cevaplanabilir olmali.
    /// </remarks>
    public static CanonicalRequest Apply(
        CanonicalRequest previous,
        CanonicalRequestDelta delta,
        string newRequestId)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(delta);
        ArgumentException.ThrowIfNullOrWhiteSpace(newRequestId);

        return new CanonicalRequest
        {
            RequestId = newRequestId,
            ConversationId = previous.ConversationId,
            PreviousRequestId = previous.RequestId,
            Source = delta.Source ?? previous.Source,
            Intent = delta.Intent ?? previous.Intent,
            Metrics = delta.Metrics ?? previous.Metrics,
            Dimensions = delta.Dimensions ?? previous.Dimensions,
            Filters = ApplyFilters(previous.Filters, delta),
            DateRange = delta.DateRange ?? previous.DateRange,
            Grain = delta.Grain ?? previous.Grain,
            Limit = delta.Limit ?? previous.Limit,
            OrderBy = delta.OrderBy ?? previous.OrderBy,
            OrderDirection = delta.OrderDirection ?? previous.OrderDirection,
            ScenarioKey = previous.ScenarioKey,
            Confidence = previous.Confidence,
            UnresolvedTerms = []
        };
    }

    private static IReadOnlyList<RequestFilter> ApplyFilters(
        IReadOnlyList<RequestFilter> previous,
        CanonicalRequestDelta delta)
    {
        var filters = previous.ToList();

        if (delta.FilterFieldsToRemove is { Count: > 0 })
        {
            filters.RemoveAll(filter =>
                delta.FilterFieldsToRemove.Contains(filter.Field, StringComparer.OrdinalIgnoreCase));
        }

        if (delta.FiltersToAdd is { Count: > 0 })
        {
            foreach (var incoming in delta.FiltersToAdd)
            {
                // Ayni alana ikinci bir filtre eklemek, birbiriyle celisen iki kosul
                // (ornek: region = 'A' AND region = 'B') uretip her zaman bos sonuc verirdi.
                // Kullanicinin niyeti daralt degil DEGISTIR olarak yorumlanir.
                filters.RemoveAll(existing =>
                    existing.Field.Equals(incoming.Field, StringComparison.OrdinalIgnoreCase));

                filters.Add(incoming);
            }
        }

        return filters;
    }
}
