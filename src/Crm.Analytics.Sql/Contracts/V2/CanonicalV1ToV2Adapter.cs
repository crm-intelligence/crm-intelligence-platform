namespace Crm.Analytics.Sql.Contracts.V2;

/// <summary>
/// Adapts the current persisted/public Canonical V1 shape to the backend-internal V2 query
/// representation without changing V1 serialization or SQL production behavior.
/// </summary>
internal static class CanonicalV1ToV2Adapter
{
    public static CanonicalQuery Adapt(CanonicalRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var ordering = string.IsNullOrWhiteSpace(request.OrderBy)
            ? Array.Empty<CanonicalOrdering>()
            :
            [
                new CanonicalOrdering
                {
                    TargetKind = CanonicalOrderingTargetKind.Dimension,
                    TargetKey = request.OrderBy,
                    Direction = request.OrderDirection
                }
            ];

        return new CanonicalQuery
        {
            Metrics = request.Metrics.ToArray(),
            Dimensions = request.Dimensions.ToArray(),
            Filters = request.Filters.Select(CloneFilter).ToArray(),
            Time = new CanonicalTimeIntent
            {
                Range = request.DateRange with { },
                Grain = request.Grain
            },
            Ordering = ordering,
            Limit = request.Limit is { } count
                ? new CanonicalLimit
                {
                    Count = count,
                    Kind = string.IsNullOrWhiteSpace(request.OrderBy)
                        ? CanonicalLimitKind.First
                        : request.OrderDirection == SortDirection.Desc
                            ? CanonicalLimitKind.Top
                            : CanonicalLimitKind.Bottom
                }
                : null
        };
    }

    private static RequestFilter CloneFilter(RequestFilter filter) => filter with
    {
        Values = filter.Values.Select(value => value with { }).ToArray()
    };
}
