using CrmAnalytics.Domain.Conversations;

namespace CrmAnalytics.UnitTests;

public sealed class ConversationTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 7, 29, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_ValidDetails_CreatesNormalizedConversation()
    {
        var conversation = CreateConversation(
            teamsConversationId: " teams-conversation-1 ",
            lastRequestId: " request-1 ");

        Assert.Equal("conversation-1", conversation.Id);
        Assert.Equal(
            "teams-conversation-1",
            conversation.TeamsConversationId);
        Assert.Equal("request-1", conversation.LastRequestId);
        Assert.Equal("user-1", conversation.UserId);
    }

    [Fact]
    public void Create_ValidDetails_UsesMatchingTimestamps()
    {
        var conversation = CreateConversation();

        Assert.Equal(CreatedAt, conversation.CreatedAt);
        Assert.Equal(conversation.CreatedAt, conversation.UpdatedAt);
    }

    [Fact]
    public void Create_EmptyTeamsConversationId_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => CreateConversation(teamsConversationId: " "));
    }

    [Fact]
    public void Create_EmptyLastRequestId_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => CreateConversation(lastRequestId: " "));
    }

    [Fact]
    public void Create_NonUtcTimestamp_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => Conversation.Create(
                id: "conversation-1",
                teamsConversationId: "teams-conversation-1",
                lastRequestId: "request-1",
                createdAt: CreatedAt.ToOffset(TimeSpan.FromHours(3))));
    }

    [Fact]
    public void UpdateLastRequest_ValidDetails_UpdatesRequestAndTimestamp()
    {
        var conversation = CreateConversation();
        var updatedAt = CreatedAt.AddMinutes(1);

        conversation.UpdateLastRequest(" request-2 ", updatedAt);

        Assert.Equal("request-2", conversation.LastRequestId);
        Assert.Equal(updatedAt, conversation.UpdatedAt);
    }

    [Fact]
    public void UpdateLastRequest_NonUtcTimestamp_ThrowsAndPreservesState()
    {
        var conversation = CreateConversation();
        var originalRequestId = conversation.LastRequestId;
        var originalUpdatedAt = conversation.UpdatedAt;

        Assert.Throws<ArgumentException>(
            () => conversation.UpdateLastRequest(
                "request-2",
                CreatedAt.ToOffset(TimeSpan.FromHours(3))));

        Assert.Equal(originalRequestId, conversation.LastRequestId);
        Assert.Equal(originalUpdatedAt, conversation.UpdatedAt);
    }

    [Fact]
    public void UpdateLastRequest_EarlierTimestamp_ThrowsAndPreservesState()
    {
        var conversation = CreateConversation();
        var originalRequestId = conversation.LastRequestId;
        var originalUpdatedAt = conversation.UpdatedAt;

        Assert.Throws<ArgumentOutOfRangeException>(
            () => conversation.UpdateLastRequest(
                "request-2",
                CreatedAt.AddTicks(-1)));

        Assert.Equal(originalRequestId, conversation.LastRequestId);
        Assert.Equal(originalUpdatedAt, conversation.UpdatedAt);
    }

    [Fact]
    public void UpdateLastRequest_EmptyRequestId_ThrowsAndPreservesState()
    {
        var conversation = CreateConversation();
        var originalRequestId = conversation.LastRequestId;
        var originalUpdatedAt = conversation.UpdatedAt;

        Assert.Throws<ArgumentException>(
            () => conversation.UpdateLastRequest(" ", CreatedAt.AddMinutes(1)));

        Assert.Equal(originalRequestId, conversation.LastRequestId);
        Assert.Equal(originalUpdatedAt, conversation.UpdatedAt);
    }

    private static Conversation CreateConversation(
        string teamsConversationId = "teams-conversation-1",
        string lastRequestId = "request-1")
    {
        return Conversation.Create(
            id: "conversation-1",
            teamsConversationId: teamsConversationId,
            lastRequestId: lastRequestId,
            createdAt: CreatedAt,
            userId: " user-1 ");
    }
}
