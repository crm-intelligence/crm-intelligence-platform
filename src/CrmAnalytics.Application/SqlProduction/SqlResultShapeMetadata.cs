namespace CrmAnalytics.Application.SqlProduction;

public sealed record SqlResultShapeMetadata(
    string? SuggestedVisual,
    string? Rationale,
    IReadOnlyCollection<SqlResultColumnMetadata> Columns,
    IReadOnlyCollection<string> Dimensions,
    IReadOnlyCollection<string> Metrics,
    IReadOnlyCollection<string> DeterministicOrder);

public sealed record SqlResultColumnMetadata(
    string Name,
    string? Unit,
    string? Format,
    string? Label = null,
    bool IsTimeDimension = false);
