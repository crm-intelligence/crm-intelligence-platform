using Crm.Analytics.Sql.Contracts;

namespace Crm.Analytics.Sql.Routing;

internal enum QueryCapabilityOutcome
{
    Deterministic,
    AgenticRequired,
    Unsupported
}

internal enum QueryComplexity
{
    Simple,
    Moderate,
    Complex,
    VeryComplex
}

internal enum QueryFeature
{
    MultipleMetrics,
    MultipleDimensions,
    Filters,
    TimeRange,
    TimeGrain,
    Ordering,
    MultipleOrderings,
    MetricOrdering,
    Limit,
    PeriodComparison,
    Calculation
}

internal enum QueryCapabilityReasonCode
{
    DeterministicCapabilitiesSatisfied,
    UnsupportedCanonicalVersion,
    EmptyProjection,
    UnknownMetric,
    UnusableMetric,
    UnknownDimension,
    UnknownFilterDimension,
    InvalidFilter,
    InvalidTime,
    SemanticOperationNotAllowed,
    IncompatibleMetricDimension,
    IncompatibleMetricFilter,
    UnapprovedMultiSourceCombination,
    InvalidOrdering,
    InvalidLimit,
    InvalidPeriodComparison,
    InvalidCalculation,
    TimeGrainRequiresAgentic,
    PeriodComparisonRequiresAgentic,
    CalculationRequiresAgentic,
    MetricOrderingRequiresAgentic,
    MultipleOrderingsRequireAgentic
}

internal sealed record QueryStrategyDecision
{
    public required QueryCapabilityOutcome Outcome { get; init; }

    public required QueryComplexity Complexity { get; init; }

    public required IReadOnlyList<QueryFeature> Features { get; init; }

    public required IReadOnlyList<QueryCapabilityReasonCode> Reasons { get; init; }

    /// <summary>
    /// Preserves the fail-closed V1 response category when no executable strategy exists.
    /// This is not a security decision and never permits SQL generation.
    /// </summary>
    public required ReasonCode FailureReasonCode { get; init; }
}
