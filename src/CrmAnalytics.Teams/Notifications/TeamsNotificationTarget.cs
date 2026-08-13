namespace CrmAnalytics.Teams.Notifications;

public sealed record TeamsNotificationTarget
{
    public TeamsNotificationTarget(
        string requestId,
        string conversationId,
        DateTimeOffset registeredAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);

        if (registeredAt.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "The registration time must be in UTC.",
                nameof(registeredAt));
        }

        RequestId = requestId.Trim();
        ConversationId = conversationId.Trim();
        RegisteredAt = registeredAt;
    }

    public string RequestId { get; }

    public string ConversationId { get; }

    public DateTimeOffset RegisteredAt { get; }
}
