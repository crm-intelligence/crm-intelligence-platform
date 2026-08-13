namespace CrmAnalytics.Teams.Messaging;

public interface ITeamsCardActionSubmissionStore
{
    Task<bool> IsProcessedAsync(
        string actionToken,
        CancellationToken cancellationToken);

    Task MarkProcessedAsync(
        string actionToken,
        CancellationToken cancellationToken);
}
