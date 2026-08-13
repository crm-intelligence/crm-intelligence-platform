using Microsoft.Teams.Cards;

namespace CrmAnalytics.Teams.Notifications;

public sealed class ReportNotificationCard
{
    public ReportNotificationCard(
        AdaptiveCard card,
        AdaptiveCard? fallbackCard = null)
    {
        ArgumentNullException.ThrowIfNull(card);
        Card = card;
        FallbackCard = fallbackCard;
    }

    public AdaptiveCard Card { get; }
    public AdaptiveCard? FallbackCard { get; }
}
