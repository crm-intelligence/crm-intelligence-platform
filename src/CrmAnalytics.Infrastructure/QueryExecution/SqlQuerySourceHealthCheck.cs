using CrmAnalytics.Application.SqlProduction;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CrmAnalytics.Infrastructure.QueryExecution;

public sealed class SqlQuerySourceHealthCheck : IHealthCheck
{
    private readonly ISqlQueryConnectionFactory _connectionFactory;
    private readonly SqlDataSource _source;

    public SqlQuerySourceHealthCheck(
        ISqlQueryConnectionFactory connectionFactory,
        SqlDataSource source)
    {
        _connectionFactory = connectionFactory;
        _source = source;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection =
                await _connectionFactory.CreateOpenConnectionAsync(
                    _source,
                    cancellationToken);
            return HealthCheckResult.Healthy(
                $"{_source} query source connection is available.");
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return HealthCheckResult.Unhealthy(
                $"{_source} query source connection is unavailable.");
        }
    }
}
