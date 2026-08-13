using CrmAnalytics.Application.Abstractions.Integrations;
using CrmAnalytics.Application.Analytics;
using CrmAnalytics.Contracts.Integrations;

namespace CrmAnalytics.Infrastructure.Integrations;

public sealed class DirectAnalyticsClient : IAnalyticsClient
{
    public Task<AnalyticsResponse> AnalyzeAsync(
        AnalyticsExecutionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var query = request.QueryResult;
        if (query is null)
        {
            return Task.FromResult(new AnalyticsResponse(
                $"direct-{request.Request.RequestId}",
                ExternalOperationStatus.Failed,
                null,
                null,
                new ExternalServiceError(
                    "DIRECT_RESULT_UNAVAILABLE",
                    "The bounded query result is unavailable for direct analytics.",
                    false)));
        }

        var truncation = query.IsTruncated ? "truncated" : "not truncated";
        var summary = $"Direct result metadata: {query.RowCount} rows; "
            + $"source {query.Source}; {truncation}.";
        return Task.FromResult(new AnalyticsResponse(
            $"direct-{request.Request.RequestId}",
            ExternalOperationStatus.Completed,
            query.ResultReference,
            summary,
            null));
    }
}
