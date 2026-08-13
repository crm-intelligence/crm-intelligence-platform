namespace CrmAnalytics.Contracts.Integrations;

/// <summary>
/// Transport-safe semantic plan supplied by an external planner. It carries no
/// physical source, SQL, authorization, data-scope or execution-policy fields.
/// </summary>
public sealed record SubmittedSemanticPlanningResult(
    string? Outcome,
    SubmittedSemanticIntent? SemanticIntent,
    IReadOnlyList<SubmittedUnresolvedConcept> UnresolvedConcepts,
    SubmittedPlanningClarification? Clarification);

public sealed record SubmittedSemanticIntent(
    string? Metric,
    IReadOnlyList<string?> GroupBy,
    IReadOnlyList<SubmittedSemanticFilter> Filters,
    SubmittedDateIntent Date,
    SubmittedRankingIntent? Ranking);

public sealed record SubmittedSemanticFilter(
    string? Dimension,
    string? Operator,
    IReadOnlyList<string?> Values);

public sealed record SubmittedDateIntent(
    string? Kind,
    string? RelativeExpression,
    int? Count,
    string? From,
    string? To,
    string? Grain);

public sealed record SubmittedRankingIntent(
    int TopN,
    string? OrderBy,
    string? Direction);

public sealed record SubmittedUnresolvedConcept(string? Kind);

public sealed record SubmittedPlanningClarification(string? Kind);
