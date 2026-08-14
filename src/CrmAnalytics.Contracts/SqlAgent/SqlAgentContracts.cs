using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace CrmAnalytics.Contracts.SqlAgent;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CopilotSqlAgentIntent
{
    [Range(2, 2)]
    public int Version { get; init; } = 2;

    [Required, MinLength(1), MaxLength(16)]
    public required IReadOnlyList<string> Metrics { get; init; }

    [Required, MaxLength(16)]
    public required IReadOnlyList<string> Dimensions { get; init; }

    [Required, MaxLength(16)]
    public required IReadOnlyList<SqlAgentFilter> Filters { get; init; }

    [Required]
    public required SqlAgentTimeIntent Time { get; init; }

    [Required, MaxLength(4)]
    public IReadOnlyList<SqlAgentPeriodComparison> Comparisons { get; init; } = [];

    [Required, MaxLength(8)]
    public IReadOnlyList<SqlAgentCalculation> Calculations { get; init; } = [];

    [Required, MaxLength(4)]
    public IReadOnlyList<SqlAgentOrdering> Ordering { get; init; } = [];

    public SqlAgentLimit? Limit { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SqlAgentFilter
{
    [Required, StringLength(128, MinimumLength = 1)]
    public required string Field { get; init; }

    public required SqlAgentFilterOperator Operator { get; init; }

    [Required, MinLength(1), MaxLength(32)]
    public required IReadOnlyList<SqlAgentFilterLiteral> Values { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SqlAgentFilterLiteral
{
    public required SqlAgentFilterValueKind Kind { get; init; }

    [Required, StringLength(512, MinimumLength = 1)]
    public required string Value { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SqlAgentTimeIntent
{
    [Required]
    public required SqlAgentDateRange Range { get; init; }

    public SqlAgentTimeGrain Grain { get; init; } = SqlAgentTimeGrain.None;
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SqlAgentDateRange
{
    public required SqlAgentDateRangeKind Kind { get; init; }

    [StringLength(128)]
    public string? RelativeExpression { get; init; }

    public DateOnly? From { get; init; }

    public DateOnly? To { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SqlAgentPeriodComparison
{
    public required SqlAgentPeriodComparisonKind Kind { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SqlAgentCalculation
{
    public required SqlAgentCalculationKind Kind { get; init; }

    [Required, MinLength(1), MaxLength(16)]
    public required IReadOnlyList<string> MetricKeys { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SqlAgentOrdering
{
    public required SqlAgentOrderingTargetKind TargetKind { get; init; }

    [Required, StringLength(128, MinimumLength = 1)]
    public required string TargetKey { get; init; }

    public SqlAgentSortDirection Direction { get; init; } = SqlAgentSortDirection.Asc;
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SqlAgentLimit
{
    [Range(1, 5000)]
    public required int Count { get; init; }

    public required SqlAgentLimitKind Kind { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public record SqlAgentIntentRequest
{
    [Required, StringLength(64, MinimumLength = 1)]
    public required string RequestId { get; init; }

    [Required]
    public required CopilotSqlAgentIntent Intent { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SqlAgentMetricContextRequest : SqlAgentIntentRequest
{
    [Required, StringLength(128, MinimumLength = 1)]
    public required string MetricKey { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SqlAgentDimensionContextRequest : SqlAgentIntentRequest
{
    [Required, StringLength(128, MinimumLength = 1)]
    public required string DimensionKey { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SqlAgentSourceContextRequest : SqlAgentIntentRequest
{
    [Required, StringLength(256, MinimumLength = 1)]
    public required string LogicalSource { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SqlAgentRelationshipsRequest : SqlAgentIntentRequest
{
    [Required, MinLength(1), MaxLength(8)]
    public required IReadOnlyList<string> LogicalSources { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SqlAgentFindJoinPathsRequest : SqlAgentIntentRequest
{
    [Required, StringLength(256, MinimumLength = 1)]
    public required string FromSource { get; init; }

    [Required, StringLength(256, MinimumLength = 1)]
    public required string ToSource { get; init; }

    [Range(1, 8)]
    public int MaxHops { get; init; } = 3;
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SqlAgentValidateJoinPathRequest : SqlAgentIntentRequest
{
    [Required, StringLength(256, MinimumLength = 1)]
    public required string FromSource { get; init; }

    [Required, StringLength(256, MinimumLength = 1)]
    public required string ToSource { get; init; }

    [Required, MaxLength(8)]
    public required IReadOnlyList<SqlAgentJoinStepProposal> Steps { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SqlAgentJoinStepProposal
{
    [Required, StringLength(128, MinimumLength = 1)]
    public required string RelationshipId { get; init; }

    [Required, StringLength(256, MinimumLength = 1)]
    public required string FromSource { get; init; }

    [Required, StringLength(256, MinimumLength = 1)]
    public required string ToSource { get; init; }

    public required SqlAgentJoinType JoinType { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SqlAgentCandidateSubmissionRequest : SqlAgentIntentRequest
{
    [Required, StringLength(64, MinimumLength = 64)]
    public required string ContextFingerprint { get; init; }

    [Required, StringLength(8000, MinimumLength = 1)]
    public required string CandidateSql { get; init; }

    [Required, MinLength(1), MaxLength(64)]
    public required IReadOnlyList<string> ReferencedSemanticKeys { get; init; }

    [Required, MaxLength(32)]
    public required IReadOnlyList<string> ReferencedRelationshipIds { get; init; }
}

public sealed record SqlAgentCapabilityResponse(
    string Status,
    string Outcome,
    string Complexity,
    IReadOnlyList<string> Features,
    IReadOnlyList<string> Reasons,
    string? ContextFingerprint);

public sealed record SqlAgentQueryContextResponse(
    string Status,
    string ContextFingerprint,
    IReadOnlyList<SqlAgentMetricContextResponse> Metrics,
    IReadOnlyList<SqlAgentDimensionContextResponse> Dimensions,
    IReadOnlyList<SqlAgentDimensionContextResponse> TimeDimensions,
    IReadOnlyList<SqlAgentSourceContextResponse> Sources,
    IReadOnlyList<SqlAgentRelationshipResponse> Relationships,
    SqlAgentContextCapabilitiesResponse Capabilities);

public sealed record SqlAgentMetricContextResponse(
    string Key,
    string Label,
    string Description,
    string Kind,
    string ValueType,
    string? Unit,
    string ApprovedExpression,
    IReadOnlyList<string> ApprovedExpressionColumns,
    string LogicalSource,
    IReadOnlyList<string> CompatibleDimensions,
    IReadOnlyList<string> CompatibleFilters,
    bool RequiresDateRange);

public sealed record SqlAgentDimensionContextResponse(
    string Key,
    string Label,
    string Description,
    string LogicalSource,
    string ApprovedPhysicalColumn,
    string ValueType,
    bool Selectable,
    bool Groupable,
    bool Filterable,
    bool Sortable,
    bool IsTimeDimension,
    string? SemanticRole);

public sealed record SqlAgentSourceContextResponse(
    string LogicalSource,
    string ApprovedPhysicalObject,
    string Runtime,
    IReadOnlyList<string> ApprovedColumns);

public sealed record SqlAgentRelationshipResponse(
    string Id,
    string LeftSource,
    string LeftPhysicalObject,
    string LeftColumn,
    string RightSource,
    string RightPhysicalObject,
    string RightColumn,
    string Cardinality,
    IReadOnlyList<string> AllowedJoinTypes);

public sealed record SqlAgentRelationshipsResponse(
    string Status,
    IReadOnlyList<SqlAgentRelationshipResponse> Relationships);

public sealed record SqlAgentJoinPathStepResponse(
    string RelationshipId,
    string FromSource,
    string ToSource,
    string JoinType);

public sealed record SqlAgentJoinPathResponse(
    IReadOnlyList<SqlAgentJoinPathStepResponse> Steps);

public sealed record SqlAgentJoinPathsResponse(
    string Status,
    IReadOnlyList<SqlAgentJoinPathResponse> Paths);

public sealed record SqlAgentJoinPathValidationResponse(
    string Status,
    string? ReasonCode);

public sealed record SqlAgentContextCapabilitiesResponse(
    int MaximumExecutableJoins,
    int ApprovedRelationshipCount,
    bool JoinExecutionEnabled,
    bool ApprovedPathDiscoveryAvailable);

public sealed record SqlAgentCandidateSubmissionResponse(
    string Status,
    string Strategy,
    string Source,
    string VerifiedPhysicalObject,
    int RowLimit,
    int CommandTimeoutSeconds);

public sealed record SqlAgentErrorResponse(string Status, string ReasonCode);

[JsonConverter(typeof(JsonStringEnumConverter<SqlAgentFilterOperator>))]
public enum SqlAgentFilterOperator
{
    [JsonStringEnumMemberName("eq")] Eq,
    [JsonStringEnumMemberName("not_eq")] NotEq,
    [JsonStringEnumMemberName("in")] In,
    [JsonStringEnumMemberName("not_in")] NotIn,
    [JsonStringEnumMemberName("gt")] Gt,
    [JsonStringEnumMemberName("gte")] Gte,
    [JsonStringEnumMemberName("lt")] Lt,
    [JsonStringEnumMemberName("lte")] Lte,
    [JsonStringEnumMemberName("between")] Between
}

[JsonConverter(typeof(JsonStringEnumConverter<SqlAgentFilterValueKind>))]
public enum SqlAgentFilterValueKind
{
    [JsonStringEnumMemberName("text")] Text,
    [JsonStringEnumMemberName("integer")] Integer,
    [JsonStringEnumMemberName("decimal")] Decimal,
    [JsonStringEnumMemberName("boolean")] Boolean,
    [JsonStringEnumMemberName("date")] Date
}

[JsonConverter(typeof(JsonStringEnumConverter<SqlAgentDateRangeKind>))]
public enum SqlAgentDateRangeKind
{
    [JsonStringEnumMemberName("relative")] Relative,
    [JsonStringEnumMemberName("absolute")] Absolute,
    [JsonStringEnumMemberName("not_applicable")] NotApplicable
}

[JsonConverter(typeof(JsonStringEnumConverter<SqlAgentTimeGrain>))]
public enum SqlAgentTimeGrain
{
    [JsonStringEnumMemberName("none")] None,
    [JsonStringEnumMemberName("day")] Day,
    [JsonStringEnumMemberName("week")] Week,
    [JsonStringEnumMemberName("month")] Month,
    [JsonStringEnumMemberName("quarter")] Quarter,
    [JsonStringEnumMemberName("year")] Year
}

[JsonConverter(typeof(JsonStringEnumConverter<SqlAgentPeriodComparisonKind>))]
public enum SqlAgentPeriodComparisonKind
{
    [JsonStringEnumMemberName("previous_period")] PreviousPeriod,
    [JsonStringEnumMemberName("previous_year")] PreviousYear
}

[JsonConverter(typeof(JsonStringEnumConverter<SqlAgentCalculationKind>))]
public enum SqlAgentCalculationKind
{
    [JsonStringEnumMemberName("difference")] Difference,
    [JsonStringEnumMemberName("percentage_change")] PercentageChange
}

[JsonConverter(typeof(JsonStringEnumConverter<SqlAgentOrderingTargetKind>))]
public enum SqlAgentOrderingTargetKind
{
    [JsonStringEnumMemberName("metric")] Metric,
    [JsonStringEnumMemberName("dimension")] Dimension
}

[JsonConverter(typeof(JsonStringEnumConverter<SqlAgentSortDirection>))]
public enum SqlAgentSortDirection
{
    [JsonStringEnumMemberName("asc")] Asc,
    [JsonStringEnumMemberName("desc")] Desc
}

[JsonConverter(typeof(JsonStringEnumConverter<SqlAgentLimitKind>))]
public enum SqlAgentLimitKind
{
    [JsonStringEnumMemberName("first")] First,
    [JsonStringEnumMemberName("top")] Top,
    [JsonStringEnumMemberName("bottom")] Bottom
}

[JsonConverter(typeof(JsonStringEnumConverter<SqlAgentJoinType>))]
public enum SqlAgentJoinType
{
    [JsonStringEnumMemberName("inner")] Inner,
    [JsonStringEnumMemberName("left")] Left
}
