using CrmAnalytics.Domain.ReportRequests;
using CrmAnalytics.Infrastructure.Persistence;

namespace CrmAnalytics.UnitTests;

public sealed class InMemoryReportRequestRepositoryTests
{
    [Fact]
    public async Task UpdateAsync_ExistingRequest_UpdatesSameRequestId()
    {
        var repository = new InMemoryReportRequestRepository();
        var reportRequest = CreateReportRequest();
        await repository.AddAsync(
            reportRequest,
            CancellationToken.None);
        reportRequest.TransitionTo(
            ReportRequestStatus.Validating,
            reportRequest.UpdatedAt.AddMinutes(1));

        await repository.UpdateAsync(
            reportRequest,
            CancellationToken.None);

        var storedReportRequest = await repository.GetByIdAsync(
            reportRequest.RequestId,
            CancellationToken.None);
        Assert.Same(reportRequest, storedReportRequest);
        Assert.Equal(
            ReportRequestStatus.Validating,
            storedReportRequest?.Status);
    }

    [Fact]
    public async Task UpdateAsync_MissingRequest_ThrowsAndDoesNotAdd()
    {
        var repository = new InMemoryReportRequestRepository();
        var reportRequest = CreateReportRequest();

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => repository.UpdateAsync(
                reportRequest,
                CancellationToken.None));

        var storedReportRequest = await repository.GetByIdAsync(
            reportRequest.RequestId,
            CancellationToken.None);
        Assert.Null(storedReportRequest);
    }

    [Fact]
    public async Task UpdateAsync_CancelledToken_CancelsOperation()
    {
        var repository = new InMemoryReportRequestRepository();
        var reportRequest = CreateReportRequest();
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => repository.UpdateAsync(
                reportRequest,
                cancellationTokenSource.Token));
    }

    [Fact]
    public async Task GetByConversationIdAsync_ReturnsOldestToNewest()
    {
        var repository = new InMemoryReportRequestRepository();
        var first = CreateReportRequest("request-1", "conversation-1");
        var second = CreateReportRequest("request-2", "CONVERSATION-1");
        await repository.AddAsync(second, CancellationToken.None);
        await repository.AddAsync(first, CancellationToken.None);

        var history = await repository.GetByConversationIdAsync(
            "Conversation-1",
            CancellationToken.None);

        Assert.Collection(
            history,
            item => Assert.Equal(first.RequestId, item.RequestId),
            item => Assert.Equal(second.RequestId, item.RequestId));
    }

    [Fact]
    public async Task GetByConversationIdAsync_MissingConversation_ReturnsEmpty()
    {
        var repository = new InMemoryReportRequestRepository();

        var history = await repository.GetByConversationIdAsync(
            "missing-conversation",
            CancellationToken.None);

        Assert.Empty(history);
    }

    private static ReportRequest CreateReportRequest()
    {
        return CreateReportRequest(
            Guid.NewGuid().ToString("N"),
            "conversation-1");
    }

    private static ReportRequest CreateReportRequest(
        string requestId,
        string conversationId)
    {
        return ReportRequest.Create(
            requestId: requestId,
            conversationId: conversationId,
            previousRequestId: null,
            prompt: "Create a CRM report.",
            correlationId: "correlation-1");
    }
}
