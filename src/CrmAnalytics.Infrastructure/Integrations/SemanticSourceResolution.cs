using Crm.Analytics.Sql.Catalog;
using Crm.Analytics.Sql.Contracts;

namespace CrmAnalytics.Infrastructure.Integrations;

public enum SemanticSourceDecisionKind
{
    Resolved,
    Ambiguous,
    Incompatible,
    Missing
}

public enum SemanticSourceEvidenceKind
{
    MetricSupported,
    DimensionSupported,
    FilterSupported,
    QueryMappingAvailable,
    ExplicitSourceConstraint
}

public sealed record SemanticSourceDecision(
    SemanticSourceDecisionKind Kind,
    DataSource? Source,
    IReadOnlyList<DataSource> CandidateSources,
    IReadOnlyList<SemanticSourceEvidenceKind> EvidenceKinds);

public interface ISemanticSourceResolver
{
    SemanticSourceDecision Resolve(
        DataSource? requestedSource,
        string? metric,
        IReadOnlyList<string> dimensions,
        IReadOnlyList<RequestFilter> filters);
}

/// <summary>
/// Intersects authoritative catalog support and deterministic query-mapping availability.
/// It cannot infer a source from wording and cannot expose physical mapping metadata.
/// </summary>
public sealed class CatalogSemanticSourceResolver(SemanticCatalogRegistry registry)
    : ISemanticSourceResolver
{
    public SemanticSourceDecision Resolve(
        DataSource? requestedSource,
        string? metric,
        IReadOnlyList<string> dimensions,
        IReadOnlyList<RequestFilter> filters)
    {
        if (metric is null)
        {
            return requestedSource is not null
                && registry.Sources.ContainsKey(requestedSource.Value)
                ? new SemanticSourceDecision(
                    SemanticSourceDecisionKind.Resolved,
                    requestedSource,
                    [requestedSource.Value],
                    [SemanticSourceEvidenceKind.ExplicitSourceConstraint])
                : new SemanticSourceDecision(
                    SemanticSourceDecisionKind.Missing, null, [], []);
        }

        var evidence = new HashSet<SemanticSourceEvidenceKind>
        {
            SemanticSourceEvidenceKind.MetricSupported,
            SemanticSourceEvidenceKind.QueryMappingAvailable
        };
        if (dimensions.Count > 0)
        {
            evidence.Add(SemanticSourceEvidenceKind.DimensionSupported);
        }
        if (filters.Count > 0)
        {
            evidence.Add(SemanticSourceEvidenceKind.FilterSupported);
        }
        if (requestedSource is not null)
        {
            evidence.Add(SemanticSourceEvidenceKind.ExplicitSourceConstraint);
        }

        var candidates = registry.Sources
            .Where(source => Supports(source.Value.Catalog, metric, dimensions, filters))
            .Select(source => source.Key)
            .Where(source => requestedSource is null || source == requestedSource)
            .Order()
            .ToArray();
        return candidates.Length switch
        {
            0 => new SemanticSourceDecision(
                SemanticSourceDecisionKind.Incompatible,
                null,
                [],
                evidence.Order().ToArray()),
            1 => new SemanticSourceDecision(
                SemanticSourceDecisionKind.Resolved,
                candidates[0],
                candidates,
                evidence.Order().ToArray()),
            _ => new SemanticSourceDecision(
                SemanticSourceDecisionKind.Ambiguous,
                null,
                candidates,
                evidence.Order().ToArray())
        };
    }

    private static bool Supports(
        MetricCatalogDocument catalog,
        string metricKey,
        IReadOnlyList<string> dimensions,
        IReadOnlyList<RequestFilter> filters)
    {
        var metric = catalog.FindMetric(metricKey);
        return metric is { IsUsable: true }
            && !string.IsNullOrWhiteSpace(metric.QueryMappingReference)
            && dimensions.All(dimension =>
                catalog.FindDimension(dimension) is { } definition
                && !string.IsNullOrWhiteSpace(definition.QueryMappingReference)
                && catalog.IsMetricDimensionCompatible(metricKey, dimension))
            && filters.All(filter =>
                catalog.FindDimension(filter.Field) is { } definition
                && !string.IsNullOrWhiteSpace(definition.QueryMappingReference)
                && catalog.IsMetricFilterCompatible(metricKey, filter.Field));
    }
}
