using CrmAnalytics.Application.SqlProduction;
using CrmAnalytics.Contracts.Integrations;

namespace CrmAnalytics.Application.Analytics;

public sealed record AnalyticsExecutionRequest(
    AnalyticsRequest Request,
    QueryExecutionResult? QueryResult)
{
    public override string ToString() =>
        "AnalyticsExecutionRequest { Query data redacted }";
}
