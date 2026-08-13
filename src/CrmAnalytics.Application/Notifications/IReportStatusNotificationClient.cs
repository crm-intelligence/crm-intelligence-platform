using CrmAnalytics.Contracts.Notifications;

namespace CrmAnalytics.Application.Notifications;

public interface IReportStatusNotificationClient
{
    Task NotifyAsync(
        ReportStatusNotificationRequest notification,
        CancellationToken cancellationToken);
}
