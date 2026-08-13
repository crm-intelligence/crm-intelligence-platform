using System.Text.Json;
using System.Text.Json.Serialization;

namespace Crm.Analytics.Sql.Catalog;

/// <summary>
/// KPI, metrik, boyut ve is sozlugunun makine tarafindan okunabilir tanimi.
/// </summary>
/// <remarks>
/// Katalog bir <b>enjeksiyon yuzeyidir</b>: <c>expression</c> alanlari serbest SQL metni
/// tasir. Bu yuzden dosya kod gibi ele alinir ve <see cref="CatalogValidator"/> tarafindan
/// yukleme aninda parse edilip allow-list'e karsi dogrulanir. Katalog'a yazilan bir satir,
/// dogrulanmazsa tum guardrail'i baypas edebilirdi.
/// </remarks>
public sealed class MetricCatalogDocument
{
    [JsonPropertyName("_meta")]
    public JsonElement? Meta { get; init; }

    public required IReadOnlyDictionary<string, MetricDefinition> Metrics { get; init; }

    public required IReadOnlyDictionary<string, DimensionDefinition> Dimensions { get; init; }

    public IReadOnlyDictionary<string, UseCaseDefinition> UseCases { get; init; } =
        new Dictionary<string, UseCaseDefinition>();

    public MetricDefinition? FindMetric(string key) =>
        Metrics.TryGetValue(key, out var metric) ? metric : null;

    public DimensionDefinition? FindDimension(string key) =>
        Dimensions.TryGetValue(key, out var dimension) ? dimension : null;

    public bool IsMetricDimensionCompatible(string metricKey, string dimensionKey)
    {
        var metric = FindMetric(metricKey);
        var dimension = FindDimension(dimensionKey);
        return metric is not null
            && dimension is not null
            && metric.CompatibleDimensions.Contains(dimensionKey, StringComparer.Ordinal)
            && dimension.CompatibleMetrics.Contains(metricKey, StringComparer.Ordinal);
    }

    public bool IsMetricFilterCompatible(string metricKey, string dimensionKey)
    {
        var metric = FindMetric(metricKey);
        var dimension = FindDimension(dimensionKey);
        return metric is not null
            && dimension is not null
            && dimension.Filterable
            && metric.CompatibleFilters.Contains(dimensionKey, StringComparer.Ordinal)
            && dimension.CompatibleMetrics.Contains(metricKey, StringComparer.Ordinal);
    }
}

/// <summary>Tek bir metrik tanimi.</summary>
public sealed class MetricDefinition
{
    public required string Label { get; init; }

    /// <summary>Model-facing business meaning. Must not contain physical schema names.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>aggregate | ratio | periodOverPeriod</summary>
    public string Kind { get; init; } = "aggregate";

    /// <summary>
    /// SQL ifadesi. <b>Tablo alias'i icermez</b>, yalnizca kolon adi yazilir; nitelendirme
    /// Query Builder tarafindan AST uzerinde yapilir. null ise metrik henuz tanimlanmamistir
    /// ve kullanilamaz.
    /// </summary>
    public string? Expression { get; init; }

    public required string Source { get; init; }

    public string? Unit { get; init; }

    /// <summary>decimal | integer | currency | percent | duration</summary>
    public string ValueType { get; init; } = "decimal";

    public string? Format { get; init; }

    public IReadOnlyList<string> Aliases { get; init; } = [];

    /// <summary>Semantic dimension keys that may be grouped with this metric.</summary>
    public IReadOnlyList<string> CompatibleDimensions { get; init; } = [];

    /// <summary>Semantic dimension keys that may filter this metric.</summary>
    public IReadOnlyList<string> CompatibleFilters { get; init; } = [];

    /// <summary>Whether a bounded date range is mandatory for this metric.</summary>
    public bool RequiresDateRange { get; init; }

    /// <summary>
    /// Opaque backend mapping key. It is validated against the semantic key and is never
    /// exposed in the model-facing catalog projection.
    /// </summary>
    public string QueryMappingReference { get; init; } = string.Empty;

    /// <summary>documented | designPending | businessApprovalPending</summary>
    public required string ApprovalStatus { get; init; }

    public string? ApprovalNote { get; init; }

    public IReadOnlyList<string> DependsOn { get; init; } = [];

    /// <summary>Metrik sorguda kullanilabilir mi (ifadesi tanimli mi).</summary>
    public bool IsUsable => !string.IsNullOrWhiteSpace(Expression);
}

/// <summary>Tek bir boyut tanimi.</summary>
public sealed class DimensionDefinition
{
    public required string Label { get; init; }

    /// <summary>Model-facing business meaning. Must not contain physical schema names.</summary>
    public string Description { get; init; } = string.Empty;

    public required string Column { get; init; }

    public required string Source { get; init; }

    /// <summary>text | integer | decimal | boolean | date</summary>
    public required string ValueType { get; init; }

    public IReadOnlyList<string> Aliases { get; init; } = [];

    /// <summary>Semantic metric keys that may use this dimension.</summary>
    public IReadOnlyList<string> CompatibleMetrics { get; init; } = [];

    /// <summary>Opaque deterministic backend mapping key; never model-facing.</summary>
    public string QueryMappingReference { get; init; } = string.Empty;

    /// <summary>Veri kapsaminin zorlandigi boyut mu.</summary>
    public bool IsScopeDimension { get; init; }

    /// <summary>Zaman ekseni boyutu mu (tarih araligi bu kolona uygulanir).</summary>
    public bool IsTimeDimension { get; init; }

    public string? Note { get; init; }

    public bool Selectable { get; init; } = true;

    public bool Filterable { get; init; } = true;

    public bool Groupable { get; init; } = true;

    public bool Sortable { get; init; } = true;

    public IReadOnlyList<string> AggregateFunctions { get; init; } = [];

    public string? SemanticRole { get; init; }
}

/// <summary>Use case tanimi ve engel durumu.</summary>
public sealed class UseCaseDefinition
{
    public required string Label { get; init; }

    /// <summary>ready | partial | blocked | impossible</summary>
    public required string Status { get; init; }

    public IReadOnlyList<string> Metrics { get; init; } = [];

    public IReadOnlyList<string> Dimensions { get; init; } = [];

    public string? Note { get; init; }

    public string? BlockedReason { get; init; }

    public IReadOnlyList<string> NeedsFromDataEngineer { get; init; } = [];

    public IReadOnlyList<string> NeedsFromBusiness { get; init; } = [];

    public bool IsUsable => Status is "ready" or "partial";
}
