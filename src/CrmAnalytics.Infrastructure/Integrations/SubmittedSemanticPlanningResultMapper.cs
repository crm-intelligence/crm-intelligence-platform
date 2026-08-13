using System.Globalization;
using Crm.Analytics.Sql.Contracts;
using CrmAnalytics.Contracts.Integrations;

namespace CrmAnalytics.Infrastructure.Integrations;

internal static class SubmittedSemanticPlanningResultMapper
{
    public static bool TryMap(
        SubmittedSemanticPlanningResult submitted,
        out ExtractedSemanticPlanningResult? result)
    {
        result = null;
        if (!TryParseOutcome(submitted.Outcome, out var outcome)
            || submitted.UnresolvedConcepts is null
            || !TryMapUnresolved(
                submitted.UnresolvedConcepts, out var unresolved)
            || !TryMapClarification(
                submitted.Clarification, out var clarification))
        {
            return false;
        }

        ExtractedSemanticIntent? intent = null;
        if (outcome == PlanningOutcome.Accepted
            && !TryMapIntent(submitted.SemanticIntent, out intent))
        {
            return false;
        }

        result = new ExtractedSemanticPlanningResult(
            outcome, intent, unresolved, clarification);
        return true;
    }

    private static bool TryMapIntent(
        SubmittedSemanticIntent? submitted,
        out ExtractedSemanticIntent? intent)
    {
        intent = null;
        if (submitted is null)
        {
            return true;
        }
        if (submitted.Metric is null
            || submitted.GroupBy is null
            || submitted.Filters is null
            || submitted.Date is null
            || submitted.GroupBy.Any(item => item is null)
            || !TryMapFilters(submitted.Filters, out var filters)
            || !TryMapDate(submitted.Date, out var date)
            || !TryMapRanking(submitted.Ranking, out var ranking))
        {
            return false;
        }

        intent = new ExtractedSemanticIntent(
            submitted.Metric,
            submitted.GroupBy.Cast<string>().ToArray(),
            filters,
            date!,
            ranking);
        return true;
    }

    private static bool TryMapFilters(
        IReadOnlyList<SubmittedSemanticFilter> submitted,
        out IReadOnlyList<ExtractedSemanticFilter> filters)
    {
        var mapped = new List<ExtractedSemanticFilter>(submitted.Count);
        foreach (var filter in submitted)
        {
            if (filter is null
                || filter.Dimension is null
                || filter.Values is null
                || filter.Values.Any(item => item is null)
                || !TryParseOperator(filter.Operator, out var op))
            {
                filters = [];
                return false;
            }
            mapped.Add(new ExtractedSemanticFilter(
                filter.Dimension,
                op,
                filter.Values.Cast<string>().ToArray()));
        }
        filters = mapped;
        return true;
    }

    private static bool TryMapDate(
        SubmittedDateIntent submitted,
        out ExtractedDateIntent? date)
    {
        date = null;
        if (!TryParseDateKind(submitted.Kind, out var kind)
            || !TryParseGrain(submitted.Grain, out var grain)
            || !TryParseDate(submitted.From, out var from)
            || !TryParseDate(submitted.To, out var to))
        {
            return false;
        }
        date = new ExtractedDateIntent(
            kind,
            submitted.RelativeExpression,
            submitted.Count,
            from,
            to,
            grain);
        return true;
    }

    private static bool TryMapRanking(
        SubmittedRankingIntent? submitted,
        out ExtractedRankingIntent? ranking)
    {
        ranking = null;
        if (submitted is null)
        {
            return true;
        }
        if (submitted.OrderBy is null
            || !TryParseDirection(submitted.Direction, out var direction))
        {
            return false;
        }
        ranking = new ExtractedRankingIntent(
            submitted.TopN, submitted.OrderBy, direction);
        return true;
    }

    private static bool TryMapUnresolved(
        IReadOnlyList<SubmittedUnresolvedConcept> submitted,
        out IReadOnlyList<UnresolvedConcept> unresolved)
    {
        var mapped = new List<UnresolvedConcept>(submitted.Count);
        foreach (var concept in submitted)
        {
            if (concept is null
                || !TryParseConceptKind(concept.Kind, out var kind))
            {
                unresolved = [];
                return false;
            }
            mapped.Add(new UnresolvedConcept(kind));
        }
        unresolved = mapped;
        return true;
    }

    private static bool TryMapClarification(
        SubmittedPlanningClarification? submitted,
        out PlanningClarification? clarification)
    {
        clarification = null;
        if (submitted is null)
        {
            return true;
        }
        if (!TryParseConceptKind(submitted.Kind, out var kind))
        {
            return false;
        }
        clarification = new PlanningClarification(kind);
        return true;
    }

    private static bool TryParseDate(string? value, out DateOnly? date)
    {
        date = null;
        if (value is null)
        {
            return true;
        }
        if (!DateOnly.TryParseExact(value, "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None,
                out var parsed))
        {
            return false;
        }
        date = parsed;
        return true;
    }

    private static bool TryParseOutcome(
        string? value, out PlanningOutcome outcome)
    {
        outcome = value switch
        {
            "accepted" => PlanningOutcome.Accepted,
            "needs_clarification" => PlanningOutcome.NeedsClarification,
            "unsupported" => PlanningOutcome.Unsupported,
            _ => (PlanningOutcome)(-1)
        };
        return Enum.IsDefined(outcome);
    }

    private static bool TryParseConceptKind(
        string? value, out UnresolvedConceptKind kind)
    {
        kind = value switch
        {
            "metric" => UnresolvedConceptKind.Metric,
            "dimension" => UnresolvedConceptKind.Dimension,
            "filter" => UnresolvedConceptKind.Filter,
            "date" => UnresolvedConceptKind.Date,
            "source" => UnresolvedConceptKind.Source,
            _ => (UnresolvedConceptKind)(-1)
        };
        return Enum.IsDefined(kind);
    }

    private static bool TryParseDateKind(
        string? value, out ExtractedDateKind kind)
    {
        kind = value switch
        {
            "unspecified" => ExtractedDateKind.Unspecified,
            "relative" => ExtractedDateKind.Relative,
            "absolute" => ExtractedDateKind.Absolute,
            _ => (ExtractedDateKind)(-1)
        };
        return Enum.IsDefined(kind);
    }

    private static bool TryParseOperator(
        string? value, out FilterOperator op)
    {
        op = value switch
        {
            "eq" => FilterOperator.Eq,
            "not_eq" => FilterOperator.NotEq,
            "in" => FilterOperator.In,
            "not_in" => FilterOperator.NotIn,
            "gt" => FilterOperator.Gt,
            "gte" => FilterOperator.Gte,
            "lt" => FilterOperator.Lt,
            "lte" => FilterOperator.Lte,
            "between" => FilterOperator.Between,
            _ => (FilterOperator)(-1)
        };
        return Enum.IsDefined(op);
    }

    private static bool TryParseGrain(string? value, out TimeGrain grain)
    {
        grain = value switch
        {
            "none" => TimeGrain.None,
            "day" => TimeGrain.Day,
            "week" => TimeGrain.Week,
            "month" => TimeGrain.Month,
            "quarter" => TimeGrain.Quarter,
            "year" => TimeGrain.Year,
            _ => (TimeGrain)(-1)
        };
        return Enum.IsDefined(grain);
    }

    private static bool TryParseDirection(
        string? value, out SortDirection direction)
    {
        direction = value switch
        {
            "asc" => SortDirection.Asc,
            "desc" => SortDirection.Desc,
            _ => (SortDirection)(-1)
        };
        return Enum.IsDefined(direction);
    }
}
