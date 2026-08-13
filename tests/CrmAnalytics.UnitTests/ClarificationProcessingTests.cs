using CrmAnalytics.Application.Abstractions.Integrations;
using CrmAnalytics.Application.Analytics;
using CrmAnalytics.Application.Conversations;
using CrmAnalytics.Application.ReportProcessing;
using CrmAnalytics.Application.ReportRequests;
using CrmAnalytics.Contracts.Integrations;
using CrmAnalytics.Domain.ReportRequests;
using CrmAnalytics.Infrastructure.Persistence;

namespace CrmAnalytics.UnitTests;

public sealed class ClarificationProcessingTests
{
    [Fact]
    public async Task ProcessAsync_WaitingWithResponse_UsesSeparateFieldsAndCompletes()
    {
        var repository = new InMemoryReportRequestRepository();
        var service = CreateService(repository);
        var created = await service.CreateAsync(
            new CreateReportRequestCommand(
                "Original sensitive prompt",
                "conversation-1",
                null,
                "correlation-1"),
            CancellationToken.None);
        await service.TransitionStatusAsync(
            new TransitionReportRequestStatusCommand(
                created.RequestId,
                ReportRequestStatus.Validating,
                DateTimeOffset.UtcNow),
            CancellationToken.None);
        await service.RequestClarificationAsync(
            new RequestReportClarificationCommand(
                created.RequestId,
                "Hangi dönem?",
                DateTimeOffset.UtcNow),
            CancellationToken.None);
        await service.SubmitClarificationAsync(
            new SubmitReportClarificationCommand(
                created.RequestId,
                "  2026 ilk çeyrek  ",
                DateTimeOffset.UtcNow),
            CancellationToken.None);
        var planner = new RecordingPlanner();
        var processing = new ReportProcessingService(
            repository,
            service,
            planner,
            new SuccessfulAnalyticsClient(),
            new SuccessfulReportClient());

        var result = await processing.ProcessAsync(
            new ProcessReportRequestCommand(
                created.RequestId,
                TestDataScopeFactory.Create()),
            CancellationToken.None);

        Assert.Equal(ReportRequestStatus.Completed, result.Status);
        Assert.NotNull(planner.Request);
        Assert.Equal(
            "Original sensitive prompt",
            planner.Request.Prompt);
        Assert.Equal(
            "2026 ilk çeyrek",
            planner.Request.ClarificationResponse);
        Assert.DoesNotContain(
            planner.Request.ClarificationResponse!,
            planner.Request.Prompt,
            StringComparison.Ordinal);
        var stored = await repository.GetByIdAsync(
            created.RequestId,
            CancellationToken.None);
        Assert.Null(stored!.ClarificationResponse);
    }

    [Fact]
    public async Task ProcessAsync_WaitingWithoutResponse_IsRejected()
    {
        var repository = new InMemoryReportRequestRepository();
        var service = CreateService(repository);
        var created = await service.CreateAsync(
            new CreateReportRequestCommand(
                "Original prompt",
                "conversation-1",
                null,
                "correlation-1"),
            CancellationToken.None);
        await service.TransitionStatusAsync(
            new TransitionReportRequestStatusCommand(
                created.RequestId,
                ReportRequestStatus.Validating,
                DateTimeOffset.UtcNow),
            CancellationToken.None);
        await service.RequestClarificationAsync(
            new RequestReportClarificationCommand(
                created.RequestId,
                "Hangi dönem?",
                DateTimeOffset.UtcNow),
            CancellationToken.None);
        var processing = new ReportProcessingService(
            repository,
            service,
            new RecordingPlanner(),
            new SuccessfulAnalyticsClient(),
            new SuccessfulReportClient());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => processing.ProcessAsync(
                new ProcessReportRequestCommand(
                    created.RequestId,
                    TestDataScopeFactory.Create()),
                CancellationToken.None));
    }

    private static ReportRequestService CreateService(
        InMemoryReportRequestRepository repository) =>
        new(
            repository,
            new ConversationContextService(
                new InMemoryConversationRepository(),
                repository));

    private sealed class RecordingPlanner : IQueryPlanningClient
    {
        public QueryPlanningRequest? Request { get; private set; }

        public Task<QueryPlanningResponse> PlanAsync(
            QueryPlanningRequest request,
            CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new QueryPlanningResponse(
                ExternalOperationStatus.Completed,
                "{}",
                null,
                "query://result",
                null));
        }
    }

    private sealed class SuccessfulAnalyticsClient : IAnalyticsClient
    {
        public Task<AnalyticsResponse> AnalyzeAsync(
            AnalyticsExecutionRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new AnalyticsResponse(
                "job-1",
                ExternalOperationStatus.Completed,
                "analysis://result",
                "Safe summary",
                null));
    }

    private sealed class SuccessfulReportClient : IReportClient
    {
        public Task<ReportGenerationResponse> GenerateAsync(
            ReportGenerationRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ReportGenerationResponse(
                "report-1",
                "Report",
                "https://app.powerbi.com/report/1",
                ExternalOperationStatus.Completed,
                null));
    }
}
