using CrmAnalytics.Application.Conversations;
using CrmAnalytics.Application.ReportProcessing;
using CrmAnalytics.Application.ReportRequests;
using CrmAnalytics.Contracts.Integrations;
using CrmAnalytics.Domain.ReportRequests;
using CrmAnalytics.Infrastructure.Persistence;
using CrmAnalytics.Infrastructure.ReportProcessing;
using Microsoft.Extensions.Options;

namespace CrmAnalytics.UnitTests;

public sealed class ClarificationSubmissionServiceTests
{
    [Fact]
    public async Task SubmitClarificationAndResume_DoesNotDirectlyQueue()
    {
        var repository = new InMemoryReportRequestRepository();
        var conversation = new ConversationContextService(
            new InMemoryConversationRepository(),
            repository);
        var requestService = new ReportRequestService(
            repository,
            conversation);
        var created = await requestService.CreateAsync(
            new CreateReportRequestCommand(
                "Original prompt",
                "conversation-1",
                null,
                "create-correlation"),
            CancellationToken.None);
        await requestService.TransitionStatusAsync(
            new TransitionReportRequestStatusCommand(
                created.RequestId,
                ReportRequestStatus.Validating,
                DateTimeOffset.UtcNow),
            CancellationToken.None);
        await requestService.RequestClarificationAsync(
            new RequestReportClarificationCommand(
                created.RequestId,
                "Hangi dönem?",
                DateTimeOffset.UtcNow),
            CancellationToken.None);
        var queue = new ThrowingQueue();
        var submission = new ReportRequestSubmissionService(requestService);

        var result = await submission.SubmitClarificationAndResumeAsync(
            new SubmitReportClarificationCommand(
                created.RequestId,
                "2026 ilk çeyrek",
                DateTimeOffset.UtcNow),
            "resume-correlation",
            UserDataScope.Empty,
            CancellationToken.None);
        var requests = await repository.GetByConversationIdAsync(
            "conversation-1",
            CancellationToken.None);

        Assert.Equal(created.RequestId, result.RequestId);
        Assert.False(queue.EnqueueCalled);
        Assert.Single(requests);
        Assert.Equal(
            created.RequestId,
            await conversation.GetLastRequestIdAsync(
                "conversation-1",
                CancellationToken.None));
    }

    [Fact]
    public async Task SubmitClarificationAndResume_QueueDisabled_SavesOnly()
    {
        var repository = new InMemoryReportRequestRepository();
        var conversation = new ConversationContextService(
            new InMemoryConversationRepository(),
            repository);
        var requestService = new ReportRequestService(
            repository,
            conversation);
        var created = await requestService.CreateAsync(
            new CreateReportRequestCommand(
                "Original prompt",
                "conversation-1",
                null,
                "create-correlation"),
            CancellationToken.None);
        await requestService.TransitionStatusAsync(
            new TransitionReportRequestStatusCommand(
                created.RequestId,
                ReportRequestStatus.Validating,
                DateTimeOffset.UtcNow),
            CancellationToken.None);
        await requestService.RequestClarificationAsync(
            new RequestReportClarificationCommand(
                created.RequestId,
                "Hangi dönem?",
                DateTimeOffset.UtcNow),
            CancellationToken.None);
        var queue = new ThrowingQueue();
        var submission = new ReportRequestSubmissionService(requestService);

        var result = await submission.SubmitClarificationAndResumeAsync(
            new SubmitReportClarificationCommand(
                created.RequestId,
                "2026 ilk çeyrek",
                DateTimeOffset.UtcNow),
            "resume-correlation",
            UserDataScope.Empty,
            CancellationToken.None);

        Assert.Equal(
            ReportRequestStatus.WaitingForClarification,
            result.Status);
        Assert.False(queue.EnqueueCalled);
    }

    private sealed class ThrowingQueue : IReportProcessingQueue
    {
        public bool EnqueueCalled { get; private set; }

        public ValueTask EnqueueAsync(
            QueuedReportProcessingRequest request,
            CancellationToken cancellationToken)
        {
            EnqueueCalled = true;
            throw new InvalidOperationException();
        }

        public ValueTask<QueuedReportProcessingRequest> DequeueAsync(
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
