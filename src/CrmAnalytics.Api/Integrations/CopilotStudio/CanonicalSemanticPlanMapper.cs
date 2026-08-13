using System.Globalization;
using Crm.Analytics.Sql.Contracts;
using CrmAnalytics.Contracts.Integrations;

namespace CrmAnalytics.Api.Integrations.CopilotStudio;

internal static class CanonicalSemanticPlanMapper
{
    public static SubmittedSemanticPlanningResult? TryMap(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var canonical = CanonicalRequestSerializer.Deserialize(json);
            if (canonical.Metrics.Count != 1) return null;
            var ranking = canonical.Limit is > 0
                && !string.IsNullOrWhiteSpace(canonical.OrderBy)
                ? new SubmittedRankingIntent(
                    canonical.Limit.Value,
                    canonical.OrderBy,
                    SnakeCase(canonical.OrderDirection.ToString()))
                : null;
            return new SubmittedSemanticPlanningResult(
                "accepted",
                new SubmittedSemanticIntent(
                    canonical.Metrics[0],
                    canonical.Dimensions.Cast<string?>().ToArray(),
                    canonical.Filters.Select(filter =>
                        new SubmittedSemanticFilter(
                            filter.Field,
                            SnakeCase(filter.Op.ToString()),
                            filter.Values.Select(value =>
                                    (string?)value.Raw)
                                .ToArray()))
                        .ToArray(),
                    new SubmittedDateIntent(
                        canonical.DateRange.Kind ==
                            DateRangeKind.NotApplicable
                            ? "unspecified"
                            : "absolute",
                        null,
                        null,
                        canonical.DateRange.From?.ToString(
                            "yyyy-MM-dd", CultureInfo.InvariantCulture),
                        canonical.DateRange.To?.ToString(
                            "yyyy-MM-dd", CultureInfo.InvariantCulture),
                        SnakeCase(canonical.Grain.ToString())),
                    ranking),
                [],
                null);
        }
        catch
        {
            return null;
        }
    }

    private static string SnakeCase(string value) => string.Concat(
        value.SelectMany((character, index) =>
            char.IsUpper(character) && index > 0
                ? new[] { '_', char.ToLowerInvariant(character) }
                : new[] { char.ToLowerInvariant(character) }));
}
