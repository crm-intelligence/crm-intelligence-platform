using CrmAnalytics.Application.Analytics;
using CrmAnalytics.Contracts.Integrations;

namespace CrmAnalytics.Application.Abstractions.Integrations;

public interface IAnalyticsClient
{
    Task<AnalyticsResponse> AnalyzeAsync(
        AnalyticsExecutionRequest request,
        CancellationToken cancellationToken);
}
