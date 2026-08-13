using CrmAnalytics.Contracts.Notifications;

namespace CrmAnalytics.Teams.Notifications;

public interface IReportNotificationDeliveryStore
{
    Task<bool> WasDeliveredAsync(
        ReportStatusNotificationRequest notification,
        CancellationToken cancellationToken);

    Task MarkDeliveredAsync(
        ReportStatusNotificationRequest notification,
        CancellationToken cancellationToken);
}
