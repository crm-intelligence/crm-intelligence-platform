using CrmAnalytics.Application.Conversations;
using CrmAnalytics.Application.Exceptions;
using CrmAnalytics.Application.Identity;
using CrmAnalytics.Application.ReportProcessing;
using CrmAnalytics.Application.ReportRequests;
using CrmAnalytics.Contracts.Integrations;
using CrmAnalytics.Domain.ReportRequests;
using CrmAnalytics.Infrastructure.Persistence;

namespace CrmAnalytics.UnitTests;

public sealed class ReportRequestSubmissionServiceTests
{
    [Fact]
    public async Task SubmitAsync_DoesNotDirectlyEnqueue()
    {
        var reportService = new FakeReportRequestService();
        var queue = new RecordingQueue();
        var service = CreateService(reportService, queue, enabled: true);
        var scope = new UserDataScope
        {
            UserId = "user-1",
            AllowedRegions = ["TR"]
        };
        var command = CreateCommand();

        var result = await service.SubmitAsync(
            command,
            scope,
            CancellationToken.None);

        Assert.True(reportService.CreateCalled);
        Assert.Equal(reportService.CreateResult, result);
        Assert.Empty(queue.Items);
    }

    [Fact]
    public async Task SubmitAsync_DisabledCreatesWithoutEnqueue()
    {
        var reportService = new FakeReportRequestService();
        var queue = new RecordingQueue();
        var service = CreateService(reportService, queue, enabled: false);

        var result = await service.SubmitAsync(
            CreateCommand(),
            UserDataScope.Empty,
            CancellationToken.None);

        Assert.True(reportService.CreateCalled);
        Assert.Equal(reportService.CreateResult, result);
        Assert.Empty(queue.Items);
    }

    [Fact]
    public async Task ReviseAndSubmitAsync_DoesNotDirectlyEnqueue()
    {
        var reportService = new FakeReportRequestService();
        var queue = new RecordingQueue();
        var service = CreateService(reportService, queue, enabled: true);
        var command = new ReviseReportRequestCommand(
            SourceRequestId: "source-request",
            Prompt: "Revise this report.",
            CorrelationId: "revision-correlation");

        var result = await service.ReviseAndSubmitAsync(
            command,
            UserDataScope.Empty,
            CancellationToken.None);

        Assert.True(reportService.ReviseCalled);
        Assert.Equal(reportService.ReviseResult, result);
        Assert.Empty(queue.Items);
    }

    [Fact]
    public async Task SubmitAsync_QueueFailureIsNotObservedBySubmission()
    {
        var expected = new InvalidOperationException("queue unavailable");
        var queue = new RecordingQueue { EnqueueException = expected };
        var service = CreateService(
            new FakeReportRequestService(),
            queue,
            enabled: true);

        var result = await service.SubmitAsync(
                CreateCommand(),
                UserDataScope.Empty,
                CancellationToken.None);

        Assert.Equal("created-request", result.RequestId);
        Assert.Empty(queue.Items);
    }

    [Fact]
    public async Task SubmitAsync_DoesNotCallQueueAndPersistsRequest()
    {
        var reportRepository = new InMemoryReportRequestRepository();
        var conversationRepository =
            new InMemoryConversationRepository();
        var conversationService = new ConversationContextService(
            conversationRepository,
            reportRepository);
        var reportService = new ReportRequestService(
            reportRepository,
            conversationService);
        var queue = new RecordingQueue
        {
            EnqueueException =
                new InvalidOperationException("queue unavailable")
        };
        var service = CreateService(reportService, queue, enabled: true);

        var result = await service.SubmitAsync(
                CreateCommand(),
                UserDataScope.Empty,
                CancellationToken.None);

        var persisted = await reportRepository.GetByIdAsync(
            result.RequestId,
            CancellationToken.None);
        Assert.NotNull(persisted);
        Assert.Equal(ReportRequestStatus.Received, persisted.Status);
    }

    [Fact]
    public async Task SubmitAsync_DoesNotPassCancellationTokenToQueue()
    {
        var queue = new RecordingQueue();
        var service = CreateService(
            new FakeReportRequestService(),
            queue,
            enabled: true);
        using var cancellationSource = new CancellationTokenSource();

        await service.SubmitAsync(
            CreateCommand(),
            UserDataScope.Empty,
            cancellationSource.Token);

        Assert.Equal(default, queue.LastCancellationToken);
    }

    [Fact]
    public async Task AuthenticatedSubmit_InvalidScopeDoesNotMutateOrQueue()
    {
        var reportService = new FakeReportRequestService();
        var queue = new RecordingQueue();
        var service = CreateService(reportService, queue, enabled: true);
        var user = new AuthenticatedUserContext(
            TestDataScopeFactory.UserId,
            TestDataScopeFactory.TenantId,
            ["Report.User"]);

        await Assert.ThrowsAsync<ForbiddenAccessException>(
            () => service.SubmitAsync(
                CreateCommand(),
                user,
                UserDataScope.Empty,
                CancellationToken.None));

        Assert.False(reportService.CreateCalled);
        Assert.Empty(queue.Items);
    }

    [Fact]
    public async Task AuthenticatedSubmit_RejectsMismatchedIdentity()
    {
        var reportService = new FakeReportRequestService();
        var queue = new RecordingQueue();
        var service = CreateService(reportService, queue, enabled: true);
        var user = new AuthenticatedUserContext(
            TestDataScopeFactory.UserId,
            TestDataScopeFactory.TenantId,
            ["Report.User"]);
        var scope = TestDataScopeFactory.Create(
            userId: "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");

        await Assert.ThrowsAsync<ForbiddenAccessException>(
            () => service.SubmitAsync(
                CreateCommand(),
                user,
                scope,
                CancellationToken.None));

        Assert.False(reportService.CreateCalled);
        Assert.Empty(queue.Items);
    }

    private static ReportRequestSubmissionService CreateService(
        IReportRequestService reportService,
        IReportProcessingQueue queue,
        bool enabled)
    {
        _ = queue;
        _ = enabled;
        return new ReportRequestSubmissionService(reportService);
    }

    private static CreateReportRequestCommand CreateCommand()
    {
        return new CreateReportRequestCommand(
            Prompt: "Create a report.",
            ConversationId: "conversation-1",
            PreviousRequestId: null,
            CorrelationId: "correlation-1");
    }

    private sealed class RecordingQueue : IReportProcessingQueue
    {
        public List<QueuedReportProcessingRequest> Items { get; } = [];

        public Exception? EnqueueException { get; init; }

        public CancellationToken LastCancellationToken { get; private set; }

        public ValueTask EnqueueAsync(
            QueuedReportProcessingRequest request,
            CancellationToken cancellationToken)
        {
            Items.Add(request);
            LastCancellationToken = cancellationToken;

            if (EnqueueException is not null)
            {
                return ValueTask.FromException(EnqueueException);
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask<QueuedReportProcessingRequest> DequeueAsync(
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FakeReportRequestService : IReportRequestService
    {
        public CreateReportRequestResult CreateResult { get; } =
            new("created-request", ReportRequestStatus.Received);

        public ReviseReportRequestResult ReviseResult { get; } =
            new(
                "new-revision-request",
                "source-request",
                "conversation-1",
                ReportRequestStatus.Received,
                DateTimeOffset.UtcNow);

        public bool CreateCalled { get; private set; }

        public bool ReviseCalled { get; private set; }

        public Task<CreateReportRequestResult> CreateAsync(
            CreateReportRequestCommand command,
            CancellationToken cancellationToken)
        {
            CreateCalled = true;
            return Task.FromResult(CreateResult);
        }

        public Task<ReviseReportRequestResult> ReviseAsync(
            ReviseReportRequestCommand command,
            CancellationToken cancellationToken)
        {
            ReviseCalled = true;
            return Task.FromResult(ReviseResult);
        }

        public Task<GetReportRequestResult?> GetByIdAsync(
            string requestId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<UpdateReportRequestResult> TransitionStatusAsync(
            TransitionReportRequestStatusCommand command,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<UpdateReportRequestResult> CompleteAsync(
            CompleteReportRequestCommand command,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<UpdateReportRequestResult> RequestClarificationAsync(
            RequestReportClarificationCommand command,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<UpdateReportRequestResult> SubmitClarificationAsync(
            SubmitReportClarificationCommand command,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<UpdateReportRequestResult> FailAsync(
            FailReportRequestCommand command,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
