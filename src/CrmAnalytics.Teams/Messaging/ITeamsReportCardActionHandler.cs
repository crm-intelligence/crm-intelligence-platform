using CrmAnalytics.Teams.Authentication;
using Microsoft.Teams.Cards;

namespace CrmAnalytics.Teams.Messaging;

public interface ITeamsReportCardActionHandler
{
    Task<AdaptiveCard> HandleAsync(
        string? verb,
        IReadOnlyDictionary<string, object?> actionData,
        string? conversationId,
        BackendApiAuthorization authorization,
        CancellationToken cancellationToken);
}
