using Crm.Analytics.Sql.Catalog;
using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.Nlu;
using Crm.Analytics.Sql.Routing;

namespace Crm.Analytics.Sql.Service;

internal sealed record SqlSourceRuntime(
    DataSource Source,
    IRequestParser Parser,
    SqlProductionRouter Router,
    MetricCatalogDocument Catalog,
    AmbiguityGate Gate)
{
    public bool Supports(CanonicalRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.Metrics.Concat(request.Dimensions).Any()
            || request.Metrics.Any(key => Catalog.FindMetric(key)?.IsUsable != true)
            || request.Dimensions.Any(key => Catalog.FindDimension(key)?.Selectable != true)
            || request.Metrics.Count > 0
                && request.Dimensions.Any(key => Catalog.FindDimension(key)?.Groupable != true)
            || request.Filters.Any(filter => Catalog.FindDimension(filter.Field)?.Filterable != true)
            || request.OrderBy is not null
                && Catalog.FindDimension(request.OrderBy)?.Sortable != true)
        {
            return false;
        }

        return request.Metrics.All(metric =>
            request.Dimensions.All(dimension =>
                Catalog.IsMetricDimensionCompatible(metric, dimension))
            && request.Filters.All(filter =>
                Catalog.IsMetricFilterCompatible(metric, filter.Field)));
    }

    public bool CanResolve(RequestParseOutcome outcome) =>
        outcome.IsSuccessful && !Gate.Evaluate(outcome.Request!).NeedsClarification;
}
