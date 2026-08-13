using CrmAnalytics.Contracts.Notifications;
using CrmAnalytics.Teams.Messaging;
using CrmAnalytics.Teams.Configuration;
using Microsoft.Extensions.Options;

namespace CrmAnalytics.Teams.Notifications;

// Production state lives in the backend SQL stores. These adapters deliberately
// hold no process state; Development can register the in-memory implementations.
internal sealed class DurableBackendNotificationTargetFallbackStore
    : ITeamsNotificationTargetStore
{
    public Task SaveAsync(TeamsNotificationTarget target,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task<TeamsNotificationTarget?> GetByRequestIdAsync(string requestId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<TeamsNotificationTarget?>(null);
    }
}

internal sealed class DurableBackendDeliveryFallbackStore
    : IReportNotificationDeliveryStore
{
    public Task<bool> WasDeliveredAsync(ReportStatusNotificationRequest value,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(false);
    }

    public Task MarkDeliveredAsync(ReportStatusNotificationRequest value,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}

internal sealed class DurableBackendActionFallbackStore
    : ITeamsCardActionSubmissionStore
{
    public Task<bool> IsProcessedAsync(string actionToken,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(false);
    }

    public Task MarkProcessedAsync(string actionToken,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}

internal sealed class ConfigurableTeamsNotificationTargetStore(
    IOptions<ReportNotificationEndpointOptions> options,
    IHostEnvironment environment,
    InMemoryTeamsNotificationTargetStore development,
    DurableBackendNotificationTargetFallbackStore durable)
    : ITeamsNotificationTargetStore
{
    private ITeamsNotificationTargetStore Current =>
        UseDurable(options.Value.Enabled, environment) ? durable : development;
    public Task SaveAsync(TeamsNotificationTarget target,
        CancellationToken cancellationToken) =>
        Current.SaveAsync(target, cancellationToken);
    public Task<TeamsNotificationTarget?> GetByRequestIdAsync(string requestId,
        CancellationToken cancellationToken) =>
        Current.GetByRequestIdAsync(requestId, cancellationToken);

    internal static bool UseDurable(bool enabled, IHostEnvironment environment) =>
        enabled && !environment.IsDevelopment()
        && !environment.IsEnvironment("Test")
        && !environment.IsEnvironment("Testing");
}

internal sealed class ConfigurableReportNotificationDeliveryStore(
    IOptions<ReportNotificationEndpointOptions> options,
    IHostEnvironment environment,
    InMemoryReportNotificationDeliveryStore development,
    DurableBackendDeliveryFallbackStore durable)
    : IReportNotificationDeliveryStore
{
    private IReportNotificationDeliveryStore Current =>
        ConfigurableTeamsNotificationTargetStore.UseDurable(
            options.Value.Enabled, environment) ? durable : development;
    public Task<bool> WasDeliveredAsync(ReportStatusNotificationRequest value,
        CancellationToken cancellationToken) =>
        Current.WasDeliveredAsync(value, cancellationToken);
    public Task MarkDeliveredAsync(ReportStatusNotificationRequest value,
        CancellationToken cancellationToken) =>
        Current.MarkDeliveredAsync(value, cancellationToken);
}

internal sealed class ConfigurableTeamsCardActionSubmissionStore(
    IOptions<ReportNotificationEndpointOptions> options,
    IHostEnvironment environment,
    InMemoryTeamsCardActionSubmissionStore development,
    DurableBackendActionFallbackStore durable)
    : ITeamsCardActionSubmissionStore
{
    private ITeamsCardActionSubmissionStore Current =>
        ConfigurableTeamsNotificationTargetStore.UseDurable(
            options.Value.Enabled, environment) ? durable : development;
    public Task<bool> IsProcessedAsync(string actionToken,
        CancellationToken cancellationToken) =>
        Current.IsProcessedAsync(actionToken, cancellationToken);
    public Task MarkProcessedAsync(string actionToken,
        CancellationToken cancellationToken) =>
        Current.MarkProcessedAsync(actionToken, cancellationToken);
}
