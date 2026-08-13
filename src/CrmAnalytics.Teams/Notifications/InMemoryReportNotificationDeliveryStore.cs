using System.Collections.Concurrent;
using CrmAnalytics.Contracts.Notifications;

namespace CrmAnalytics.Teams.Notifications;

public sealed class InMemoryReportNotificationDeliveryStore
    : IReportNotificationDeliveryStore
{
    private readonly ConcurrentDictionary<DeliveryKey, byte> _deliveries =
        new();

    public Task<bool> WasDeliveredAsync(
        ReportStatusNotificationRequest notification,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(
            _deliveries.ContainsKey(CreateKey(notification)));
    }

    public Task MarkDeliveredAsync(
        ReportStatusNotificationRequest notification,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        cancellationToken.ThrowIfCancellationRequested();

        _deliveries.TryAdd(CreateKey(notification), 0);
        return Task.CompletedTask;
    }

    private static DeliveryKey CreateKey(
        ReportStatusNotificationRequest notification)
    {
        return new DeliveryKey(
            notification.RequestId.Trim().ToUpperInvariant(),
            notification.Status,
            notification.UpdatedAt.ToUniversalTime());
    }

    private sealed record DeliveryKey(
        string RequestId,
        ReportNotificationStatus Status,
        DateTimeOffset UpdatedAt);
}
