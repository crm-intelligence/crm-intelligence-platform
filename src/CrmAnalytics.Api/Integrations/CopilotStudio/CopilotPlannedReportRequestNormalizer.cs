using CrmAnalytics.Contracts.CopilotStudio;
using CrmAnalytics.Contracts.Integrations;

namespace CrmAnalytics.Api.Integrations.CopilotStudio;

public static class CopilotPlannedReportRequestNormalizer
{
    public static SubmittedSemanticPlanningResult Normalize(
        CopilotPlannedReportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.SemanticIntent);
        ArgumentNullException.ThrowIfNull(request.UnresolvedConcepts);
        ArgumentNullException.ThrowIfNull(request.Clarification);

        return Normalize(
            request.Outcome,
            request.SemanticIntent,
            request.UnresolvedConcepts,
            request.Clarification);
    }

    public static SubmittedSemanticPlanningResult Normalize(
        CopilotSemanticPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(plan.SemanticIntent);
        ArgumentNullException.ThrowIfNull(plan.UnresolvedConcepts);
        ArgumentNullException.ThrowIfNull(plan.Clarification);

        return Normalize(
            plan.Outcome,
            plan.SemanticIntent,
            plan.UnresolvedConcepts,
            plan.Clarification);
    }

    private static SubmittedSemanticPlanningResult Normalize(
        string? rawOutcome,
        CopilotSemanticIntent semanticIntent,
        IReadOnlyList<CopilotUnresolvedConcept> unresolvedConcepts,
        CopilotClarification rawClarification)
    {
        var outcome = NullIfEmpty(rawOutcome);
        // Keep the model's partial semantic intent for Phase 2 planning
        // context. The production mapper consumes intent only for accepted
        // plans, so clarification cannot bypass backend completeness checks.
        var intent = NormalizeIntent(semanticIntent);
        var clarification = outcome == "needs_clarification"
            ? new SubmittedPlanningClarification(
                NullIfEmpty(rawClarification.Kind))
            : null;

        return new SubmittedSemanticPlanningResult(
            outcome,
            intent,
            unresolvedConcepts.Select(item =>
                new SubmittedUnresolvedConcept(
                    NullIfEmpty(item?.Kind))).ToArray(),
            clarification);
    }

    private static SubmittedSemanticIntent NormalizeIntent(
        CopilotSemanticIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent.GroupBy);
        ArgumentNullException.ThrowIfNull(intent.Filters);
        ArgumentNullException.ThrowIfNull(intent.Date);
        ArgumentNullException.ThrowIfNull(intent.Ranking);

        return new SubmittedSemanticIntent(
            NullIfEmpty(intent.Metric),
            intent.GroupBy.Select(NullIfEmpty).ToArray(),
            intent.Filters.Select(filter =>
            {
                if (filter is null)
                {
                    return new SubmittedSemanticFilter(null, null, []);
                }
                return new SubmittedSemanticFilter(
                    NullIfEmpty(filter.Dimension),
                    NullIfEmpty(filter.Operator),
                    filter.Values?.Select(NullIfEmpty).ToArray() ?? []);
            }).ToArray(),
            NormalizeDate(intent.Date),
            intent.Ranking.TopN > 0
                && NullIfEmpty(intent.Ranking.OrderBy) is not null
                && NullIfEmpty(intent.Ranking.Direction) is not null
                ? new SubmittedRankingIntent(
                    intent.Ranking.TopN,
                    NullIfEmpty(intent.Ranking.OrderBy),
                    NullIfEmpty(intent.Ranking.Direction))
                : null);
    }

    private static SubmittedDateIntent NormalizeDate(
        CopilotDateIntent date)
    {
        var relativeExpression = NullIfEmpty(date.RelativeExpression);
        int? count = date.Count == 0
            && relativeExpression?.StartsWith(
                "last_n_", StringComparison.Ordinal) != true
                ? null
                : date.Count;
        return new SubmittedDateIntent(
            NullIfEmpty(date.Kind),
            relativeExpression,
            count,
            NullIfEmpty(date.From),
            NullIfEmpty(date.To),
            NullIfEmpty(date.Grain));
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
