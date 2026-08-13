using CrmAnalytics.Contracts.InternalTeams;

namespace CrmAnalytics.Application.Notifications;

public interface IInternalTeamsNotificationClient
{
    Task NotifyAsync(InternalReportStatusNotificationRequest notification,
        CancellationToken cancellationToken);
}
