using CrmAnalytics.Application.Abstractions.Persistence;
using CrmAnalytics.Application.Conversations;
using CrmAnalytics.Application.ReportRequests;
using CrmAnalytics.Domain.ReportRequests;

namespace CrmAnalytics.UnitTests;

public sealed class ReportRequestServiceTests
{
    [Fact]
    public async Task TransitionStatusAsync_MissingRequest_ThrowsKeyNotFoundException()
    {
        var repository = new FakeReportRequestRepository();
        var service = CreateService(repository);
        var command = new TransitionReportRequestStatusCommand(
            RequestId: "missing-request",
            TargetStatus: ReportRequestStatus.Validating,
            TransitionedAt: DateTimeOffset.UtcNow);

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => service.TransitionStatusAsync(
                command,
                CancellationToken.None));

        Assert.Equal(0, repository.UpdateCallCount);
    }

    [Fact]
    public async Task TransitionStatusAsync_ValidTransition_UpdatesRepository()
    {
        var reportRequest = CreateReportRequest();
        var repository = new FakeReportRequestRepository(reportRequest);
        var service = CreateService(repository);
        var transitionedAt = reportRequest.UpdatedAt.AddMinutes(1);

        var result = await service.TransitionStatusAsync(
            new TransitionReportRequestStatusCommand(
                RequestId: reportRequest.RequestId,
                TargetStatus: ReportRequestStatus.Validating,
                TransitionedAt: transitionedAt),
            CancellationToken.None);

        Assert.Equal(1, repository.UpdateCallCount);
        Assert.Same(reportRequest, repository.UpdatedReportRequest);
        Assert.Equal(ReportRequestStatus.Validating, result.Status);
        Assert.Equal(transitionedAt, result.UpdatedAt);
    }

    [Fact]
    public async Task CompleteAsync_ValidCommand_PersistsCompletedResult()
    {
        var reportRequest = CreateInProcessingStatus();
        var repository = new FakeReportRequestRepository(reportRequest);
        var service = CreateService(repository);
        var completedAt = reportRequest.UpdatedAt.AddMinutes(1);

        var result = await service.CompleteAsync(
            new CompleteReportRequestCommand(
                RequestId: reportRequest.RequestId,
                ReportId: "report-1",
                Summary: "CRM report summary.",
                PowerBiUrl: "https://app.powerbi.com/reports/report-1",
                CompletedAt: completedAt),
            CancellationToken.None);

        Assert.Equal(1, repository.UpdateCallCount);
        Assert.Equal(ReportRequestStatus.Completed, result.Status);
        Assert.Equal(completedAt, result.UpdatedAt);
        Assert.Equal("report-1", repository.UpdatedReportRequest?.ReportId);
        Assert.Equal(
            "CRM report summary.",
            repository.UpdatedReportRequest?.Summary);
        Assert.Equal(
            "https://app.powerbi.com/reports/report-1",
            repository.UpdatedReportRequest?.PowerBiUrl);
    }

    [Fact]
    public async Task FailAsync_ValidCommand_PersistsFailureDetails()
    {
        var reportRequest = CreateReportRequest();
        var repository = new FakeReportRequestRepository(reportRequest);
        var service = CreateService(repository);
        var failedAt = reportRequest.UpdatedAt.AddMinutes(1);

        var result = await service.FailAsync(
            new FailReportRequestCommand(
                RequestId: reportRequest.RequestId,
                ErrorCode: "QUERY_SERVICE_UNAVAILABLE",
                ErrorMessage: "The report service is temporarily unavailable.",
                FailedAt: failedAt),
            CancellationToken.None);

        Assert.Equal(1, repository.UpdateCallCount);
        Assert.Equal(ReportRequestStatus.Failed, result.Status);
        Assert.Equal(failedAt, result.UpdatedAt);
        Assert.Equal(
            "QUERY_SERVICE_UNAVAILABLE",
            repository.UpdatedReportRequest?.ErrorCode);
        Assert.Equal(
            "The report service is temporarily unavailable.",
            repository.UpdatedReportRequest?.ErrorMessage);
    }

    [Fact]
    public async Task CompleteAsync_InvalidDomainTransition_DoesNotUpdateRepository()
    {
        var reportRequest = CreateReportRequest();
        var repository = new FakeReportRequestRepository(reportRequest);
        var service = CreateService(repository);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CompleteAsync(
                new CompleteReportRequestCommand(
                    RequestId: reportRequest.RequestId,
                    ReportId: "report-1",
                    Summary: "CRM report summary.",
                    PowerBiUrl:
                        "https://app.powerbi.com/reports/report-1",
                    CompletedAt:
                        reportRequest.UpdatedAt.AddMinutes(1)),
                CancellationToken.None));

        Assert.Equal(0, repository.UpdateCallCount);
        Assert.Equal(ReportRequestStatus.Received, reportRequest.Status);
    }

    [Fact]
    public async Task TransitionStatusAsync_CancelledToken_CancelsOperation()
    {
        var reportRequest = CreateReportRequest();
        var repository = new FakeReportRequestRepository(reportRequest);
        var service = CreateService(repository);
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.TransitionStatusAsync(
                new TransitionReportRequestStatusCommand(
                    RequestId: reportRequest.RequestId,
                    TargetStatus: ReportRequestStatus.Validating,
                    TransitionedAt:
                        reportRequest.UpdatedAt.AddMinutes(1)),
                cancellationTokenSource.Token));

        Assert.Equal(0, repository.UpdateCallCount);
        Assert.Equal(ReportRequestStatus.Received, reportRequest.Status);
    }

    [Fact]
    public async Task CreateAsync_AfterAddingRequest_RegistersConversation()
    {
        var repository = new FakeReportRequestRepository();
        var conversationService = new FakeConversationContextService();
        var service = new ReportRequestService(
            repository,
            conversationService);

        var result = await service.CreateAsync(
            new CreateReportRequestCommand(
                Prompt: "Create a CRM report.",
                ConversationId: "teams-conversation-1",
                PreviousRequestId: null,
                CorrelationId: "correlation-1"),
            CancellationToken.None);

        Assert.Equal(1, repository.AddCallCount);
        Assert.Equal(1, conversationService.RegisterCallCount);
        Assert.Equal(result.RequestId, conversationService.RegisteredRequestId);
        Assert.Equal(
            "teams-conversation-1",
            conversationService.RegisteredConversationId);
    }

    [Fact]
    public async Task CreateAsync_ConversationRegistrationFailure_Propagates()
    {
        var repository = new FakeReportRequestRepository();
        var conversationService = new FakeConversationContextService
        {
            RegistrationException =
                new InvalidOperationException("registration failed")
        };
        var service = new ReportRequestService(
            repository,
            conversationService);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CreateAsync(
                new CreateReportRequestCommand(
                    Prompt: "Create a CRM report.",
                    ConversationId: "teams-conversation-1",
                    PreviousRequestId: null,
                    CorrelationId: "correlation-1"),
                CancellationToken.None));

        Assert.Equal("registration failed", exception.Message);
        Assert.Equal(1, repository.AddCallCount);
        Assert.Equal(1, conversationService.RegisterCallCount);
    }

    [Fact]
    public async Task ReviseAsync_CompletedSource_CreatesIndependentReceivedRequest()
    {
        var source = CreateCompletedReportRequest();
        var sourceUpdatedAt = source.UpdatedAt;
        var repository = new FakeReportRequestRepository(source);
        var conversationService = new FakeConversationContextService();
        var service = new ReportRequestService(
            repository,
            conversationService);

        var result = await service.ReviseAsync(
            new ReviseReportRequestCommand(
                SourceRequestId: source.RequestId,
                Prompt: "Show net profit instead of sales count.",
                CorrelationId: "new-correlation"),
            CancellationToken.None);

        var revision = Assert.IsType<ReportRequest>(
            repository.AddedReportRequest);
        Assert.NotEqual(source.RequestId, result.RequestId);
        Assert.Equal(result.RequestId, revision.RequestId);
        Assert.Equal(source.RequestId, result.PreviousRequestId);
        Assert.Equal(source.RequestId, revision.PreviousRequestId);
        Assert.Equal(source.ConversationId, result.ConversationId);
        Assert.Equal(source.ConversationId, revision.ConversationId);
        Assert.Equal(ReportRequestStatus.Received, result.Status);
        Assert.Equal(ReportRequestStatus.Received, revision.Status);
        Assert.Equal(
            "Show net profit instead of sales count.",
            revision.Prompt);
        Assert.Equal("new-correlation", revision.CorrelationId);
        Assert.Null(revision.ReportId);
        Assert.Null(revision.Summary);
        Assert.Null(revision.PowerBiUrl);
        Assert.Null(revision.ErrorCode);
        Assert.Null(revision.ErrorMessage);

        Assert.Equal(ReportRequestStatus.Completed, source.Status);
        Assert.Equal(sourceUpdatedAt, source.UpdatedAt);
        Assert.Equal("report-1", source.ReportId);
        Assert.Equal("CRM report summary.", source.Summary);
        Assert.Equal(
            "https://app.powerbi.com/reports/report-1",
            source.PowerBiUrl);
        Assert.Equal("correlation-1", source.CorrelationId);

        Assert.Equal(1, repository.AddCallCount);
        Assert.Equal(1, conversationService.RegisterCallCount);
        Assert.Equal(
            revision.RequestId,
            conversationService.RegisteredRequestId);
        Assert.Equal(
            source.ConversationId,
            conversationService.RegisteredConversationId);
        Assert.Equal(
            revision.CreatedAt,
            conversationService.RegisteredAt);
        Assert.Equal(
            revision.RequestId,
            conversationService.LastRequestId);
    }

    [Fact]
    public async Task ReviseAsync_MissingSource_ThrowsAndDoesNotRegister()
    {
        var repository = new FakeReportRequestRepository();
        var conversationService = new FakeConversationContextService();
        var service = new ReportRequestService(
            repository,
            conversationService);

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => service.ReviseAsync(
                new ReviseReportRequestCommand(
                    SourceRequestId: "missing-request",
                    Prompt: "Show net profit.",
                    CorrelationId: "new-correlation"),
                CancellationToken.None));

        Assert.Equal(0, repository.AddCallCount);
        Assert.Equal(0, conversationService.RegisterCallCount);
    }

    [Theory]
    [InlineData(ReportRequestStatus.Received)]
    [InlineData(ReportRequestStatus.Processing)]
    [InlineData(ReportRequestStatus.Failed)]
    public async Task ReviseAsync_NonCompletedSource_ThrowsAndDoesNotPersist(
        ReportRequestStatus status)
    {
        var source = CreateReportRequestInStatus(status);
        var repository = new FakeReportRequestRepository(source);
        var conversationService = new FakeConversationContextService();
        var service = new ReportRequestService(
            repository,
            conversationService);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ReviseAsync(
                new ReviseReportRequestCommand(
                    SourceRequestId: source.RequestId,
                    Prompt: "Show net profit.",
                    CorrelationId: "new-correlation"),
                CancellationToken.None));

        Assert.Equal(status, source.Status);
        Assert.Equal(0, repository.AddCallCount);
        Assert.Equal(0, conversationService.RegisterCallCount);
        Assert.Null(conversationService.LastRequestId);
    }

    [Fact]
    public async Task ReviseAsync_CancelledToken_CancelsOperation()
    {
        var source = CreateCompletedReportRequest();
        var repository = new FakeReportRequestRepository(source);
        var conversationService = new FakeConversationContextService();
        var service = new ReportRequestService(
            repository,
            conversationService);
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.ReviseAsync(
                new ReviseReportRequestCommand(
                    SourceRequestId: source.RequestId,
                    Prompt: "Show net profit.",
                    CorrelationId: "new-correlation"),
                cancellationTokenSource.Token));

        Assert.Equal(0, repository.AddCallCount);
        Assert.Equal(0, conversationService.RegisterCallCount);
    }

    [Fact]
    public async Task ReviseAsync_RegistrationFailure_PropagatesWithoutChangingSource()
    {
        var source = CreateCompletedReportRequest();
        var sourceUpdatedAt = source.UpdatedAt;
        var repository = new FakeReportRequestRepository(source);
        var conversationService = new FakeConversationContextService
        {
            RegistrationException =
                new InvalidOperationException("registration failed")
        };
        var service = new ReportRequestService(
            repository,
            conversationService);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ReviseAsync(
                new ReviseReportRequestCommand(
                    SourceRequestId: source.RequestId,
                    Prompt: "Show net profit.",
                    CorrelationId: "new-correlation"),
                CancellationToken.None));

        Assert.Equal("registration failed", exception.Message);
        Assert.Equal(1, repository.AddCallCount);
        Assert.Equal(1, conversationService.RegisterCallCount);
        Assert.Equal(ReportRequestStatus.Completed, source.Status);
        Assert.Equal(sourceUpdatedAt, source.UpdatedAt);
        Assert.Equal("report-1", source.ReportId);
        Assert.Equal("CRM report summary.", source.Summary);
        Assert.Equal(
            "https://app.powerbi.com/reports/report-1",
            source.PowerBiUrl);
    }

    private static ReportRequestService CreateService(
        IReportRequestRepository repository)
    {
        return new ReportRequestService(
            repository,
            new FakeConversationContextService());
    }

    private static ReportRequest CreateReportRequest()
    {
        return ReportRequest.Create(
            requestId: Guid.NewGuid().ToString("N"),
            conversationId: "conversation-1",
            previousRequestId: null,
            prompt: "Create a CRM report.",
            correlationId: "correlation-1");
    }

    private static ReportRequest CreateInProcessingStatus()
    {
        var reportRequest = CreateReportRequest();
        reportRequest.TransitionTo(
            ReportRequestStatus.Validating,
            reportRequest.UpdatedAt.AddMinutes(1));
        reportRequest.TransitionTo(
            ReportRequestStatus.Processing,
            reportRequest.UpdatedAt.AddMinutes(1));
        return reportRequest;
    }

    private static ReportRequest CreateCompletedReportRequest()
    {
        var reportRequest = CreateInProcessingStatus();
        reportRequest.Complete(
            reportId: "report-1",
            summary: "CRM report summary.",
            powerBiUrl: "https://app.powerbi.com/reports/report-1",
            completedAt: reportRequest.UpdatedAt.AddMinutes(1));
        return reportRequest;
    }

    private static ReportRequest CreateReportRequestInStatus(
        ReportRequestStatus status)
    {
        var reportRequest = CreateReportRequest();

        switch (status)
        {
            case ReportRequestStatus.Received:
                return reportRequest;
            case ReportRequestStatus.Processing:
                reportRequest.TransitionTo(
                    ReportRequestStatus.Validating,
                    reportRequest.UpdatedAt.AddMinutes(1));
                reportRequest.TransitionTo(
                    ReportRequestStatus.Processing,
                    reportRequest.UpdatedAt.AddMinutes(1));
                return reportRequest;
            case ReportRequestStatus.Failed:
                reportRequest.Fail(
                    "TEST_FAILURE",
                    "Test failure.",
                    reportRequest.UpdatedAt.AddMinutes(1));
                return reportRequest;
            default:
                throw new ArgumentOutOfRangeException(nameof(status));
        }
    }

    private sealed class FakeReportRequestRepository
        : IReportRequestRepository
    {
        private readonly ReportRequest? _reportRequest;

        public FakeReportRequestRepository(
            ReportRequest? reportRequest = null)
        {
            _reportRequest = reportRequest;
        }

        public int UpdateCallCount { get; private set; }

        public int AddCallCount { get; private set; }

        public ReportRequest? UpdatedReportRequest { get; private set; }

        public ReportRequest? AddedReportRequest { get; private set; }

        public Task AddAsync(
            ReportRequest reportRequest,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AddCallCount++;
            AddedReportRequest = reportRequest;
            return Task.CompletedTask;
        }

        public Task<ReportRequest?> GetByIdAsync(
            string requestId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = string.Equals(
                requestId,
                _reportRequest?.RequestId,
                StringComparison.OrdinalIgnoreCase)
                ? _reportRequest
                : null;

            return Task.FromResult(result);
        }

        public Task<IReadOnlyList<ReportRequest>>
            GetByConversationIdAsync(
                string conversationId,
                CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<ReportRequest> result =
                _reportRequest is null
                    ? []
                    : [_reportRequest];
            return Task.FromResult(result);
        }

        public Task UpdateAsync(
            ReportRequest reportRequest,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            UpdateCallCount++;
            UpdatedReportRequest = reportRequest;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeConversationContextService
        : IConversationContextService
    {
        public int RegisterCallCount { get; private set; }

        public string? RegisteredConversationId { get; private set; }

        public string? RegisteredRequestId { get; private set; }

        public DateTimeOffset? RegisteredAt { get; private set; }

        public string? LastRequestId { get; private set; }

        public Exception? RegistrationException { get; init; }

        public Task RegisterRequestAsync(
            string teamsConversationId,
            string requestId,
            DateTimeOffset registeredAt,
            string? userId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RegisterCallCount++;
            RegisteredConversationId = teamsConversationId;
            RegisteredRequestId = requestId;
            RegisteredAt = registeredAt;

            if (RegistrationException is not null)
            {
                throw RegistrationException;
            }

            LastRequestId = requestId;
            return Task.CompletedTask;
        }

        public Task<string?> GetLastRequestIdAsync(
            string teamsConversationId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(LastRequestId);
        }

        public Task<IReadOnlyList<ConversationReportRequestHistoryItem>>
            GetHistoryAsync(
                string teamsConversationId,
                CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<
                IReadOnlyList<ConversationReportRequestHistoryItem>>([]);
        }
    }
}
