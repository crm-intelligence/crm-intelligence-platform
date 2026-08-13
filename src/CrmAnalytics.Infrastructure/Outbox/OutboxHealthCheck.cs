using CrmAnalytics.Infrastructure.Persistence.SqlServer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CrmAnalytics.Infrastructure.Outbox;

public sealed class OutboxHealthCheck : IHealthCheck
{
    private static readonly TimeSpan MaximumReadyAge = TimeSpan.FromMinutes(5);
    private readonly CrmAnalyticsDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public OutboxHealthCheck(CrmAnalyticsDbContext dbContext,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        var ready = _dbContext.OutboxMessages.AsNoTracking()
            .Where(item => item.Status == OutboxStatus.Pending
                && item.NextAttemptAt <= now);
        var pendingCount = await ready.CountAsync(cancellationToken);
        var oldest = await ready.MinAsync(
            item => (DateTimeOffset?)item.OccurredAt, cancellationToken);
        var deadLetteredCount = await _dbContext.OutboxMessages.AsNoTracking()
            .CountAsync(item => item.Status == OutboxStatus.DeadLettered,
                cancellationToken);

        var description =
            $"Outbox ready={pendingCount}, deadLettered={deadLetteredCount}.";
        return oldest.HasValue && now - oldest.Value > MaximumReadyAge
            ? HealthCheckResult.Unhealthy(description)
            : HealthCheckResult.Healthy(description);
    }
}
