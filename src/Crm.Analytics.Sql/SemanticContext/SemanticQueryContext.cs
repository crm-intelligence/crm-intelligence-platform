using System.Collections.Immutable;
using Crm.Analytics.Sql.Catalog;
using Crm.Analytics.Sql.Contracts;

namespace Crm.Analytics.Sql.SemanticContext;

internal sealed record SemanticQueryContext(
    DataSource Runtime,
    ImmutableArray<SemanticMetricContext> Metrics,
    ImmutableArray<SemanticDimensionContext> Dimensions,
    ImmutableArray<SemanticDimensionContext> TimeDimensions,
    ImmutableArray<SemanticSourceContext> Sources,
    ImmutableArray<ApprovedRelationshipDefinition> Relationships,
    SemanticContextCapabilities Capabilities);

internal sealed record SemanticMetricContext(
    string Key,
    string Label,
    string Description,
    ImmutableArray<string> Aliases,
    string Kind,
    string ValueType,
    string? Unit,
    string ApprovedExpression,
    ImmutableArray<string> ApprovedExpressionColumns,
    string LogicalSource,
    ImmutableArray<string> CompatibleDimensions,
    ImmutableArray<string> CompatibleFilters,
    bool RequiresDateRange);

internal sealed record SemanticDimensionContext(
    string Key,
    string Label,
    string Description,
    ImmutableArray<string> Aliases,
    string LogicalSource,
    string ApprovedPhysicalColumn,
    string ValueType,
    bool Selectable,
    bool Groupable,
    bool Filterable,
    bool Sortable,
    bool IsTimeDimension,
    string? SemanticRole);

internal sealed record SemanticSourceContext(
    string LogicalSource,
    string ApprovedPhysicalObject,
    DataSource Runtime,
    ImmutableArray<string> ApprovedColumns);

internal sealed record SemanticContextCapabilities(
    int MaximumExecutableJoins,
    int ApprovedRelationshipCount,
    bool JoinExecutionEnabled,
    bool ApprovedPathDiscoveryAvailable);

internal enum SemanticContextFailureCode
{
    InvalidCanonicalVersion,
    UnknownMetric,
    UnusableMetric,
    UnknownDimension,
    UnknownSource,
    InvalidRelationshipRequest
}

internal sealed record SemanticContextFailure(
    SemanticContextFailureCode Code,
    string SemanticKey,
    string Detail);

internal sealed record SemanticContextResult<T>(T? Value, SemanticContextFailure? Failure)
    where T : class
{
    public bool IsSuccessful => Value is not null && Failure is null;

    public static SemanticContextResult<T> Success(T value) => new(value, null);

    public static SemanticContextResult<T> Fail(
        SemanticContextFailureCode code,
        string semanticKey,
        string detail) => new(null, new SemanticContextFailure(code, semanticKey, detail));
}
