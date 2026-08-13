namespace CrmAnalytics.Application.SqlProduction;

public sealed record SqlProductionClientResult(
    SqlProductionClientDecision Decision,
    string? CanonicalRequestJson,
    string? UserMessage,
    string? RejectionCode,
    SqlExecutionPlan? ExecutionPlan,
    SqlResultShapeMetadata? ResultShape);

public enum SqlProductionClientDecision
{
    Accepted = 1,
    NeedsClarification = 2,
    Rejected = 3,
    Failed = 4
}
