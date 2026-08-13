using Crm.Analytics.Sql.Contracts;

namespace CrmAnalytics.Infrastructure.Integrations;

public interface IOllamaStructuredPlanningClient
{
    Task<OllamaStructuredPlanningResult> PlanAsync(
        OllamaStructuredPlanningRequest request,
        CancellationToken cancellationToken);

    Task<SemanticGapPlanningResult> ResolveGapsAsync(
        SemanticGapPlanningRequest request,
        CancellationToken cancellationToken) => Task.FromResult(
            SemanticGapPlanningResult.Failure("NotImplemented", "OLLAMA_GAP_NOT_IMPLEMENTED"));

    Task<OllamaSemanticPlanningResult> PlanSemanticAsync(
        OllamaSemanticPlanningRequest request,
        CancellationToken cancellationToken) => Task.FromResult(
            OllamaSemanticPlanningResult.Failure(
                "NotImplemented", "OLLAMA_SEMANTIC_NOT_IMPLEMENTED"));
}

public sealed record OllamaStructuredPlanningRequest(
    string RequestId,
    string ConversationId,
    DateOnly Today,
    string UserPrompt,
    string? OriginalPrompt = null,
    string? ClarificationQuestion = null,
    string? ClarificationAnswer = null,
    SemanticCandidateConstraints? CandidateConstraints = null);

public sealed record OllamaSemanticPlanningRequest(
    string RequestId,
    string ConversationId,
    DateOnly Today,
    string UserPrompt,
    string? OriginalPrompt = null,
    string? ClarificationQuestion = null,
    string? ClarificationAnswer = null);

public sealed record SemanticCandidateConstraints(
    IReadOnlyList<string> MetricKeys,
    IReadOnlyList<string> DimensionKeys,
    DateRangeSpec? DateRange = null,
    IReadOnlyList<string>? ResolvedMetricKeys = null,
    IReadOnlyList<string>? ResolvedDimensionKeys = null,
    IReadOnlyList<UnresolvedConceptKind>? AmbiguousSlotKinds = null,
    DataSource? Source = null)
{
    public IReadOnlyList<string> FixedMetricKeys => ResolvedMetricKeys ?? [];
    public IReadOnlyList<string> FixedDimensionKeys => ResolvedDimensionKeys ?? [];

    public bool Allows(CanonicalRequest request) =>
        request.Metrics.All(MetricKeys.Contains)
        && request.Dimensions.All(DimensionKeys.Contains)
        && request.Filters.All(filter => DimensionKeys.Contains(filter.Field))
        && (request.OrderBy is null || DimensionKeys.Contains(request.OrderBy))
        && FixedMetricKeys.All(request.Metrics.Contains)
        && FixedDimensionKeys.All(request.Dimensions.Contains)
        && (Source is null || request.Source == Source)
        && (DateRange is null || request.DateRange == DateRange);
}

public sealed record SemanticGapPlanningRequest(
    string RequestId,
    string ConversationId,
    DateOnly Today,
    string UserPrompt,
    SemanticPlanningState State,
    SemanticCandidateConstraints CandidateConstraints);

public enum SemanticGapOutcome
{
    Resolved,
    NeedsClarification
}

public sealed record SemanticGapSelection(
    SemanticGapOutcome Outcome,
    string? MetricKey,
    IReadOnlyList<string> DimensionKeys,
    PlanningClarification? Clarification);

public sealed record SemanticGapPlanningResult(
    SemanticGapSelection? Selection,
    string Outcome,
    string ReasonCode,
    long DurationMilliseconds,
    bool SchemaValidationSucceeded,
    int? PromptTokenCount = null,
    int? CompletionTokenCount = null)
{
    public static SemanticGapPlanningResult Failure(string outcome, string reasonCode,
        long durationMilliseconds = 0) =>
        new(null, outcome, reasonCode, durationMilliseconds, false);
}

public enum PlanningOutcome
{
    Accepted,
    NeedsClarification,
    Unsupported
}

public enum UnresolvedConceptKind
{
    Metric,
    Dimension,
    Filter,
    Date,
    Source
}

/// <summary>
/// Identifies only the unresolved semantic slot. It deliberately carries no raw user text.
/// </summary>
public sealed record UnresolvedConcept(UnresolvedConceptKind Kind);

/// <summary>
/// Backend-safe input for the semantic clarification mechanism. User-facing wording remains
/// backend-owned and is never supplied by the model.
/// </summary>
public sealed record PlanningClarification(UnresolvedConceptKind Kind);

public sealed record PlanningResult(
    PlanningOutcome Outcome,
    CanonicalRequest? CanonicalRequest,
    IReadOnlyList<UnresolvedConcept> UnresolvedConcepts,
    PlanningClarification? Clarification);

public enum ExtractedDateKind
{
    Unspecified,
    Relative,
    Absolute
}

public sealed record ExtractedDateIntent(
    ExtractedDateKind Kind,
    string? RelativeExpression,
    int? Count,
    DateOnly? From,
    DateOnly? To,
    TimeGrain Grain);

public sealed record ExtractedSemanticFilter(
    string Dimension,
    FilterOperator Operator,
    IReadOnlyList<string> Values);

public sealed record ExtractedRankingIntent(
    int TopN,
    string OrderBy,
    SortDirection Direction);

/// <summary>
/// Model-owned semantic extraction. It deliberately contains no source, physical object,
/// column, SQL, authorization, data-scope or execution-policy field.
/// </summary>
public sealed record ExtractedSemanticIntent(
    string Metric,
    IReadOnlyList<string> GroupBy,
    IReadOnlyList<ExtractedSemanticFilter> Filters,
    ExtractedDateIntent Date,
    ExtractedRankingIntent? Ranking);

public sealed record ExtractedSemanticPlanningResult(
    PlanningOutcome Outcome,
    ExtractedSemanticIntent? Intent,
    IReadOnlyList<UnresolvedConcept> UnresolvedConcepts,
    PlanningClarification? Clarification);

public sealed record OllamaSemanticPlanningResult(
    ExtractedSemanticPlanningResult? Plan,
    string Outcome,
    string ReasonCode,
    long DurationMilliseconds,
    bool SchemaValidationSucceeded,
    string? DoneReason,
    int? PromptTokenCount,
    int? CompletionTokenCount)
{
    public static OllamaSemanticPlanningResult Failure(
        string outcome,
        string reasonCode,
        long durationMilliseconds = 0,
        string? doneReason = null,
        int? promptTokenCount = null,
        int? completionTokenCount = null) => new(
            null, outcome, reasonCode, durationMilliseconds, false,
            doneReason, promptTokenCount, completionTokenCount);
}

public sealed record OllamaStructuredPlanningResult(
    PlanningResult? Plan,
    string Outcome,
    string ReasonCode,
    long DurationMilliseconds,
    bool SchemaValidationSucceeded,
    string? DoneReason,
    int? PromptTokenCount,
    int? CompletionTokenCount)
{
    public CanonicalRequest? CanonicalRequest => Plan?.CanonicalRequest;

    public IReadOnlyList<UnresolvedConcept> UnresolvedConcepts =>
        Plan?.UnresolvedConcepts ?? [];

    public PlanningOutcome? SemanticOutcome => Plan?.Outcome;
}
