using CrmAnalytics.Teams.Backend;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CrmAnalytics.Teams.Hosting;

internal sealed class BackendApiHealthCheck(
    IHttpClientFactory httpClientFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await httpClientFactory
                .CreateClient(ReportRequestsApiClient.HttpClientName)
                .GetAsync("/health/ready", cancellationToken);
            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy();
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return HealthCheckResult.Unhealthy();
        }
    }
}
