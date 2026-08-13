using CrmAnalytics.Teams.Notifications;

namespace CrmAnalytics.UnitTests;

public sealed class TeamsNotificationTargetStoreTests
{
    [Fact]
    public async Task SavedTarget_CanBeReadByRequestId()
    {
        var store = new InMemoryTeamsNotificationTargetStore();
        var registeredAt = DateTimeOffset.UtcNow;

        await store.SaveAsync(
            new TeamsNotificationTarget(
                "  request-1  ",
                "  conversation-1  ",
                registeredAt),
            CancellationToken.None);

        var target = await store.GetByRequestIdAsync(
            "REQUEST-1",
            CancellationToken.None);

        Assert.NotNull(target);
        Assert.Equal("request-1", target.RequestId);
        Assert.Equal("conversation-1", target.ConversationId);
        Assert.Equal(registeredAt, target.RegisteredAt);
    }

    [Fact]
    public async Task SameMapping_IsIdempotent()
    {
        var store = new InMemoryTeamsNotificationTargetStore();

        await store.SaveAsync(
            CreateTarget("request-1", "conversation-1"),
            CancellationToken.None);
        await store.SaveAsync(
            CreateTarget("REQUEST-1", "conversation-1"),
            CancellationToken.None);

        var target = await store.GetByRequestIdAsync(
            "request-1",
            CancellationToken.None);
        Assert.NotNull(target);
        Assert.Equal("conversation-1", target.ConversationId);
    }

    [Fact]
    public async Task ExistingRequest_CannotChangeConversation()
    {
        var store = new InMemoryTeamsNotificationTargetStore();
        await store.SaveAsync(
            CreateTarget("request-1", "conversation-1"),
            CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.SaveAsync(
                CreateTarget("request-1", "conversation-2"),
                CancellationToken.None));
    }

    [Fact]
    public async Task Cancellation_IsApplied()
    {
        var store = new InMemoryTeamsNotificationTargetStore();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => store.SaveAsync(
                CreateTarget("request-1", "conversation-1"),
                cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => store.GetByRequestIdAsync(
                "request-1",
                cancellation.Token));
    }

    [Fact]
    public void Target_RequiresUtcRegistrationTime()
    {
        Assert.Throws<ArgumentException>(
            () => new TeamsNotificationTarget(
                "request-1",
                "conversation-1",
                new DateTimeOffset(
                    2026,
                    7,
                    30,
                    12,
                    0,
                    0,
                    TimeSpan.FromHours(3))));
    }

    private static TeamsNotificationTarget CreateTarget(
        string requestId,
        string conversationId) =>
        new(requestId, conversationId, DateTimeOffset.UtcNow);
}
