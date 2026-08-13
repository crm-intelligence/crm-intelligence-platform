using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CrmAnalytics.Infrastructure.Persistence.SqlServer;

public sealed class CrmDatabaseHealthCheck : IHealthCheck
{
    private readonly IServiceScopeFactory _scopeFactory;

    public CrmDatabaseHealthCheck(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider
                .GetRequiredService<CrmAnalyticsDbContext>();

            return await dbContext.Database.CanConnectAsync(
                cancellationToken)
                ? HealthCheckResult.Healthy(
                    "CRM persistence database is reachable.")
                : HealthCheckResult.Unhealthy(
                    "CRM persistence database is unavailable.");
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return HealthCheckResult.Unhealthy(
                "CRM persistence database is unavailable.");
        }
    }
}
