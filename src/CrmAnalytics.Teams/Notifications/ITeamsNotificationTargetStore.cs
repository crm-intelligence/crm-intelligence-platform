namespace CrmAnalytics.Teams.Notifications;

public interface ITeamsNotificationTargetStore
{
    Task SaveAsync(
        TeamsNotificationTarget target,
        CancellationToken cancellationToken);

    Task<TeamsNotificationTarget?> GetByRequestIdAsync(
        string requestId,
        CancellationToken cancellationToken);
}
