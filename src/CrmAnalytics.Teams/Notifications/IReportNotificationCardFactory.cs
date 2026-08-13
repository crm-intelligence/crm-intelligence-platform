using CrmAnalytics.Contracts.Notifications;

namespace CrmAnalytics.Teams.Notifications;

public interface IReportNotificationCardFactory
{
    ReportNotificationCard Create(
        ReportStatusNotificationRequest notification);
}
