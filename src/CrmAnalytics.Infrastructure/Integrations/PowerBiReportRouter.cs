using System.Globalization;
using CrmAnalytics.Application.Abstractions.Integrations;
using CrmAnalytics.Application.SqlProduction;
using CrmAnalytics.Contracts.Integrations;
using Microsoft.Extensions.Options;

namespace CrmAnalytics.Infrastructure.Integrations;

public sealed class PowerBiReportRouter : IPowerBiReportRouter
{
    private static readonly IReadOnlyDictionary<string, string>
        FilterFieldMappings = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["product_category"] =
                "dim_product/product_category_name",
            ["customer_city"] = "dim_customer/customer_city",
            ["customer_state"] = "dim_customer/customer_state",
            ["order_id"] = "fact_sales/order_id"
        };

    private static readonly HashSet<string> PaymentSemantics =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "payment_total",
            "avg_installments",
            "payment_type",
            "payment_order_timestamp"
        };

    private static readonly HashSet<string> CustomerSemantics =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "customer_count",
            "avg_monetary",
            "avg_frequency",
            "recency_days",
            "rfm_customer_state",
            "rfm_customer_city"
        };

    private readonly PowerBiOptions _options;

    public PowerBiReportRouter(IOptions<ReportingOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value.PowerBi;
    }

    public PowerBiReportRoute Route(
        SqlDataSource source,
        SqlResultShapeMetadata? resultShape,
        SubmittedSemanticPlanningResult? semanticPlan)
    {
        var (reportId, pageId) = source == SqlDataSource.Oltp
            ? (_options.OltpReportId, _options.OltpPageId)
            : SelectAnalyticsRoute(resultShape);
        var workspaceId = Guid.Parse(_options.WorkspaceId)
            .ToString("D");
        reportId = Guid.Parse(reportId).ToString("D");
        var url = $"https://app.powerbi.com/groups/{workspaceId}"
            + $"/reports/{reportId}/{pageId}";

        if (source != SqlDataSource.Oltp)
        {
            var filterExpression = BuildFilterExpression(semanticPlan);
            if (filterExpression is not null)
            {
                try
                {
                    url += "?filter="
                        + Uri.EscapeDataString(filterExpression);
                }
                catch (UriFormatException)
                {
                    // A malformed Unicode value must not fail reporting.
                    // Keep the already allowlisted, unfiltered base URL.
                }
            }
        }

        return new PowerBiReportRoute(reportId, pageId, url);
    }

    private static string? BuildFilterExpression(
        SubmittedSemanticPlanningResult? semanticPlan)
    {
        if (semanticPlan?.SemanticIntent is not { } intent
            || !string.Equals(
                semanticPlan.Outcome,
                "accepted",
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var conditions = new List<string>();
        AddFullCalendarMonthConditions(intent.Date, conditions);

        if (intent.Filters is not null)
        {
            foreach (var filter in intent.Filters)
            {
                if (filter is null
                    || !string.Equals(
                        filter.Operator,
                        "eq",
                        StringComparison.OrdinalIgnoreCase)
                    || filter.Dimension is null
                    || !FilterFieldMappings.TryGetValue(
                        filter.Dimension,
                        out var field)
                    || filter.Values is not { Count: 1 }
                    || string.IsNullOrWhiteSpace(filter.Values[0]))
                {
                    continue;
                }

                var escapedValue = filter.Values[0]!
                    .Replace("'", "''", StringComparison.Ordinal);
                conditions.Add($"{field} eq '{escapedValue}'");
            }
        }

        return conditions.Count == 0
            ? null
            : string.Join(" and ", conditions);
    }

    private static void AddFullCalendarMonthConditions(
        SubmittedDateIntent? date,
        ICollection<string> conditions)
    {
        if (date is null
            || !string.Equals(
                date.Kind,
                "absolute",
                StringComparison.OrdinalIgnoreCase)
            || !DateOnly.TryParseExact(
                date.From,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var from)
            || !DateOnly.TryParseExact(
                date.To,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var to)
            || from.Day != 1
            || from.Year != to.Year
            || from.Month != to.Month
            || to.Day != DateTime.DaysInMonth(to.Year, to.Month))
        {
            return;
        }

        conditions.Add($"dim_date/calendar_year eq {from.Year}");
        conditions.Add($"dim_date/month_number eq {from.Month}");
    }

    private (string ReportId, string PageId) SelectAnalyticsRoute(
        SqlResultShapeMetadata? resultShape)
    {
        var semantics = (resultShape?.Metrics ?? [])
            .Concat(resultShape?.Dimensions ?? []);
        if (semantics.Any(PaymentSemantics.Contains))
            return (_options.ReportId, _options.PaymentAnalysisPageId);
        if (semantics.Any(CustomerSemantics.Contains))
            return (_options.ReportId,
                _options.CustomerSegmentationPageId);

        // Sales is the safe analytics default. Unknown intents never fall
        // through to the unrelated customer-segmentation page.
        return (_options.ReportId, _options.SalesAnalysisPageId);
    }
}
