using CrmAnalytics.Application.Conversations;
using CrmAnalytics.Application.Identity;
using CrmAnalytics.Domain.ReportRequests;
using CrmAnalytics.Infrastructure.Persistence;

namespace CrmAnalytics.UnitTests;

public sealed class ConversationContextServiceTests
{
    [Fact]
    public async Task RegisterRequestAsync_FirstRequest_CreatesConversation()
    {
        var (service, conversationRepository, _) = CreateService();
        var registeredAt = DateTimeOffset.UtcNow;

        await service.RegisterRequestAsync(
            "teams-conversation-1",
            "request-1",
            registeredAt,
            "user-1",
            CancellationToken.None);

        var conversation =
            await conversationRepository.GetByTeamsConversationIdAsync(
                "teams-conversation-1",
                CancellationToken.None);
        Assert.NotNull(conversation);
        Assert.Equal("request-1", conversation.LastRequestId);
        Assert.Equal("user-1", conversation.UserId);
    }

    [Fact]
    public async Task RegisterRequestAsync_SecondRequest_UpdatesExistingConversation()
    {
        var (service, conversationRepository, _) = CreateService();
        var firstRegisteredAt = DateTimeOffset.UtcNow;
        await service.RegisterRequestAsync(
            "teams-conversation-1",
            "request-1",
            firstRegisteredAt,
            null,
            CancellationToken.None);
        var firstConversation =
            await conversationRepository.GetByTeamsConversationIdAsync(
                "teams-conversation-1",
                CancellationToken.None);

        await service.RegisterRequestAsync(
            "TEAMS-CONVERSATION-1",
            "request-2",
            firstRegisteredAt.AddMinutes(1),
            null,
            CancellationToken.None);

        var updatedConversation =
            await conversationRepository.GetByTeamsConversationIdAsync(
                "teams-conversation-1",
                CancellationToken.None);
        Assert.Same(firstConversation, updatedConversation);
        Assert.Equal("request-2", updatedConversation?.LastRequestId);
    }

    [Fact]
    public async Task GetLastRequestIdAsync_ExistingConversation_ReturnsLastId()
    {
        var (service, _, _) = CreateService();
        await service.RegisterRequestAsync(
            "teams-conversation-1",
            "request-1",
            DateTimeOffset.UtcNow,
            null,
            CancellationToken.None);

        var result = await service.GetLastRequestIdAsync(
            "teams-conversation-1",
            CancellationToken.None);

        Assert.Equal("request-1", result);
    }

    [Fact]
    public async Task GetLastRequestIdAsync_MissingConversation_ReturnsNull()
    {
        var (service, _, _) = CreateService();

        var result = await service.GetLastRequestIdAsync(
            "missing-conversation",
            CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetHistoryAsync_ReturnsSortedSafeModels()
    {
        var (service, _, reportRequestRepository) = CreateService();
        var first = CreateReportRequest("request-1", null, "secret prompt 1");
        var second = CreateReportRequest(
            "request-2",
            first.RequestId,
            "secret prompt 2");
        await reportRequestRepository.AddAsync(
            second,
            CancellationToken.None);
        await reportRequestRepository.AddAsync(
            first,
            CancellationToken.None);

        var history = await service.GetHistoryAsync(
            "TEAMS-CONVERSATION-1",
            CancellationToken.None);

        Assert.Collection(
            history,
            item => Assert.Equal(first.RequestId, item.RequestId),
            item => Assert.Equal(second.RequestId, item.RequestId));
        Assert.Null(history[0].PreviousRequestId);
        Assert.Equal(first.RequestId, history[1].PreviousRequestId);
        Assert.Null(
            typeof(ConversationReportRequestHistoryItem).GetProperty("Prompt"));
    }

    [Fact]
    public async Task GetHistoryAsync_CancelledToken_CancelsOperation()
    {
        var (service, _, _) = CreateService();
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.GetHistoryAsync(
                "teams-conversation-1",
                cancellationTokenSource.Token));
    }

    [Fact]
    public async Task AuthenticatedRegistration_DifferentOwnerIsRejected()
    {
        var (service, conversationRepository, _) = CreateService();
        var registeredAt = DateTimeOffset.UtcNow;
        var owner = new AuthenticatedUserContext(
            "11111111-1111-4111-8111-111111111111",
            "22222222-2222-4222-8222-222222222222",
            Array.Empty<string>());
        await service.RegisterAuthenticatedRequestAsync(
            "bound-conversation",
            "request-owner",
            registeredAt,
            owner,
            CancellationToken.None);

        var otherUser = new AuthenticatedUserContext(
            "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
            owner.TenantId,
            ["Report.Admin"]);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RegisterAuthenticatedRequestAsync(
                "bound-conversation",
                "request-attacker",
                registeredAt.AddMinutes(1),
                otherUser,
                CancellationToken.None));

        var conversation =
            await conversationRepository.GetByTeamsConversationIdAsync(
                "bound-conversation",
                CancellationToken.None);
        Assert.Equal("request-owner", conversation?.LastRequestId);
        Assert.Equal(owner.UserId, conversation?.UserId);
        Assert.Equal(owner.TenantId, conversation?.TenantId);
    }

    private static (
        ConversationContextService Service,
        InMemoryConversationRepository ConversationRepository,
        InMemoryReportRequestRepository ReportRequestRepository)
        CreateService()
    {
        var conversationRepository = new InMemoryConversationRepository();
        var reportRequestRepository =
            new InMemoryReportRequestRepository();
        var service = new ConversationContextService(
            conversationRepository,
            reportRequestRepository);
        return (service, conversationRepository, reportRequestRepository);
    }

    private static ReportRequest CreateReportRequest(
        string requestId,
        string? previousRequestId,
        string prompt)
    {
        return ReportRequest.Create(
            requestId: requestId,
            conversationId: "teams-conversation-1",
            previousRequestId: previousRequestId,
            prompt: prompt,
            correlationId: $"correlation-{requestId}");
    }
}
