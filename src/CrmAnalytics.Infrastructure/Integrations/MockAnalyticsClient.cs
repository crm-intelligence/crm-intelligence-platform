using CrmAnalytics.Application.Abstractions.Integrations;
using CrmAnalytics.Application.Analytics;
using CrmAnalytics.Contracts.Integrations;
using Microsoft.Extensions.Options;

namespace CrmAnalytics.Infrastructure.Integrations;

public sealed class MockAnalyticsClient : IAnalyticsClient
{
    private readonly MockExternalServicesOptions _options;

    public MockAnalyticsClient(
        IOptions<MockExternalServicesOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
    }

    public async Task<AnalyticsResponse> AnalyzeAsync(
        AnalyticsExecutionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        await Task.Delay(
            _options.DelayMilliseconds,
            cancellationToken);

        var jobId = $"mock-analytics-job-{request.Request.RequestId}";

        if (_options.ForceAnalyticsFailure)
        {
            return new AnalyticsResponse(
                JobId: jobId,
                Status: ExternalOperationStatus.Failed,
                ResultReference: null,
                Summary: null,
                Error: new ExternalServiceError(
                    Code: "ANALYTICS_FAILED",
                    Message: "Analytics could not be completed.",
                    IsTransient: true));
        }

        return new AnalyticsResponse(
            JobId: jobId,
            Status: ExternalOperationStatus.Completed,
            ResultReference:
                $"analytics-result://{request.Request.RequestId}",
            Summary: "Mock analiz başarıyla tamamlandı.",
            Error: null);
    }
}
