namespace Crm.Analytics.Sql.Contracts.V2;

/// <summary>
/// Backend-internal, versioned analytical intent representation. This is not a transport
/// contract and is deliberately absent from the public Copilot and Canonical V1 schemas.
/// </summary>
internal sealed record CanonicalQuery
{
    public const int CurrentVersion = 2;

    public int Version { get; init; } = CurrentVersion;

    public required IReadOnlyList<string> Metrics { get; init; }

    public required IReadOnlyList<string> Dimensions { get; init; }

    public required IReadOnlyList<RequestFilter> Filters { get; init; }

    public required CanonicalTimeIntent Time { get; init; }

    public IReadOnlyList<CanonicalPeriodComparison> Comparisons { get; init; } = [];

    public IReadOnlyList<CanonicalCalculation> Calculations { get; init; } = [];

    public IReadOnlyList<CanonicalOrdering> Ordering { get; init; } = [];

    public CanonicalLimit? Limit { get; init; }
}

internal sealed record CanonicalTimeIntent
{
    public required DateRangeSpec Range { get; init; }

    public TimeGrain Grain { get; init; } = TimeGrain.None;
}

internal enum PeriodComparisonKind
{
    PreviousPeriod,
    PreviousYear
}

internal sealed record CanonicalPeriodComparison
{
    public required PeriodComparisonKind Kind { get; init; }
}

/// <summary>
/// Closed calculation vocabulary. Arbitrary formulas or SQL expressions are intentionally
/// impossible to represent.
/// </summary>
internal enum CanonicalCalculationKind
{
    Difference,
    PercentageChange
}

internal sealed record CanonicalCalculation
{
    public required CanonicalCalculationKind Kind { get; init; }

    public required IReadOnlyList<string> MetricKeys { get; init; }
}

internal enum CanonicalOrderingTargetKind
{
    Metric,
    Dimension
}

internal sealed record CanonicalOrdering
{
    public required CanonicalOrderingTargetKind TargetKind { get; init; }

    public required string TargetKey { get; init; }

    public SortDirection Direction { get; init; } = SortDirection.Asc;
}

internal enum CanonicalLimitKind
{
    First,
    Top,
    Bottom
}

internal sealed record CanonicalLimit
{
    public required int Count { get; init; }

    public CanonicalLimitKind Kind { get; init; } = CanonicalLimitKind.First;
}
