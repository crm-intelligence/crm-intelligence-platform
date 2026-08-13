namespace CrmAnalytics.Teams.Notifications;

public interface ITeamsProactiveNotificationSender
{
    Task SendCardAsync(
        string conversationId,
        ReportNotificationCard card,
        CancellationToken cancellationToken);
}
