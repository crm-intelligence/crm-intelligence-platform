using CrmAnalytics.Domain.Conversations;
using CrmAnalytics.Infrastructure.Persistence;

namespace CrmAnalytics.UnitTests;

public sealed class InMemoryConversationRepositoryTests
{
    [Fact]
    public async Task AddAsync_ThenGetByTeamsConversationId_ReturnsConversation()
    {
        var repository = new InMemoryConversationRepository();
        var conversation = CreateConversation();

        await repository.AddAsync(conversation, CancellationToken.None);
        var stored = await repository.GetByTeamsConversationIdAsync(
            "TEAMS-CONVERSATION-1",
            CancellationToken.None);

        Assert.Same(conversation, stored);
    }

    [Fact]
    public async Task AddAsync_DuplicateTeamsConversationId_Throws()
    {
        var repository = new InMemoryConversationRepository();
        await repository.AddAsync(
            CreateConversation(),
            CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.AddAsync(
                CreateConversation(id: "conversation-2"),
                CancellationToken.None));
    }

    [Fact]
    public async Task UpdateAsync_ExistingConversation_UpdatesRecord()
    {
        var repository = new InMemoryConversationRepository();
        var conversation = CreateConversation();
        await repository.AddAsync(conversation, CancellationToken.None);
        conversation.UpdateLastRequest(
            "request-2",
            conversation.UpdatedAt.AddMinutes(1));

        await repository.UpdateAsync(conversation, CancellationToken.None);
        var stored = await repository.GetByTeamsConversationIdAsync(
            conversation.TeamsConversationId,
            CancellationToken.None);

        Assert.Equal("request-2", stored?.LastRequestId);
    }

    [Fact]
    public async Task UpdateAsync_MissingConversation_ThrowsAndDoesNotUpsert()
    {
        var repository = new InMemoryConversationRepository();
        var conversation = CreateConversation();

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => repository.UpdateAsync(
                conversation,
                CancellationToken.None));

        Assert.Null(await repository.GetByTeamsConversationIdAsync(
            conversation.TeamsConversationId,
            CancellationToken.None));
    }

    [Fact]
    public async Task AddAsync_CancelledToken_CancelsOperation()
    {
        var repository = new InMemoryConversationRepository();
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => repository.AddAsync(
                CreateConversation(),
                cancellationTokenSource.Token));
    }

    private static Conversation CreateConversation(
        string id = "conversation-1")
    {
        return Conversation.Create(
            id: id,
            teamsConversationId: "teams-conversation-1",
            lastRequestId: "request-1",
            createdAt: new DateTimeOffset(
                2026,
                7,
                29,
                9,
                0,
                0,
                TimeSpan.Zero));
    }
}
