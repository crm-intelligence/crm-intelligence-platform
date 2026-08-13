using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace CrmAnalytics.Infrastructure.Integrations;

public sealed class ExternalProviderReadinessHealthCheck(
    IOptions<AnalyticsOptions> analytics,
    IOptions<ReportingOptions> reporting) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Accessing Value invokes startup validators. Never include configured
        // IDs or exception details in the health result.
        _ = analytics.Value.Provider;
        _ = reporting.Value.Provider;
        return Task.FromResult(HealthCheckResult.Healthy());
    }
}
