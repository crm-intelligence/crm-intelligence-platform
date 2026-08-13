using Microsoft.Teams.Apps;
using Microsoft.Teams.Cards;

namespace CrmAnalytics.Teams.Notifications;

public sealed class TeamsProactiveNotificationSender
    : ITeamsProactiveNotificationSender
{
    private readonly ITeamsSdkCardSender _sdkSender;

    public TeamsProactiveNotificationSender(App app)
        : this(new TeamsSdkCardSender(app))
    {
    }

    internal TeamsProactiveNotificationSender(
        ITeamsSdkCardSender sdkSender)
    {
        ArgumentNullException.ThrowIfNull(sdkSender);
        _sdkSender = sdkSender;
    }

    public Task SendCardAsync(
        string conversationId,
        ReportNotificationCard card,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
        ArgumentNullException.ThrowIfNull(card);
        cancellationToken.ThrowIfCancellationRequested();

        return SendWithFallbackAsync(
            conversationId.Trim(), card, cancellationToken);
    }

    private async Task SendWithFallbackAsync(
        string conversationId,
        ReportNotificationCard card,
        CancellationToken cancellationToken)
    {
        try
        {
            await _sdkSender.SendCardAsync(
                conversationId, card.Card, cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch when (card.FallbackCard is not null)
        {
            await _sdkSender.SendCardAsync(
                conversationId, card.FallbackCard, cancellationToken);
        }
    }
}

internal interface ITeamsSdkCardSender
{
    Task SendCardAsync(
        string conversationId,
        AdaptiveCard card,
        CancellationToken cancellationToken);
}

internal sealed class TeamsSdkCardSender : ITeamsSdkCardSender
{
    private readonly App _app;

    public TeamsSdkCardSender(App app)
    {
        ArgumentNullException.ThrowIfNull(app);
        _app = app;
    }

    public async Task SendCardAsync(
        string conversationId,
        AdaptiveCard card,
        CancellationToken cancellationToken)
    {
        await _app.Send(
            conversationId,
            card,
            cancellationToken: cancellationToken);
    }
}
