namespace CrmAnalytics.Domain.Conversations;

public sealed class Conversation
{
    private Conversation(
        string id,
        string teamsConversationId,
        string? userId,
        string? tenantId,
        string lastRequestId,
        DateTimeOffset createdAt)
    {
        Id = id;
        TeamsConversationId = teamsConversationId;
        UserId = userId;
        TenantId = tenantId;
        LastRequestId = lastRequestId;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public string Id { get; private set; }

    public string TeamsConversationId { get; private set; }

    public string? UserId { get; private set; }

    public string? TenantId { get; private set; }

    public string LastRequestId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Conversation Create(
        string id,
        string teamsConversationId,
        string lastRequestId,
        DateTimeOffset createdAt,
        string? userId = null,
        string? tenantId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(teamsConversationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(lastRequestId);

        if (createdAt.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "The creation time must be in UTC.",
                nameof(createdAt));
        }

        return new Conversation(
            id: id.Trim(),
            teamsConversationId: teamsConversationId.Trim(),
            userId: string.IsNullOrWhiteSpace(userId)
                ? null
                : userId.Trim(),
            tenantId: string.IsNullOrWhiteSpace(tenantId)
                ? null
                : tenantId.Trim(),
            lastRequestId: lastRequestId.Trim(),
            createdAt: createdAt);
    }

    public void UpdateLastRequest(
        string requestId,
        DateTimeOffset updatedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);

        if (updatedAt.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "The update time must be in UTC.",
                nameof(updatedAt));
        }

        if (updatedAt < UpdatedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(updatedAt),
                updatedAt,
                "The update time cannot be earlier than UpdatedAt.");
        }

        LastRequestId = requestId.Trim();
        UpdatedAt = updatedAt;
    }
}
