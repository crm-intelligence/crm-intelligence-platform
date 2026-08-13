using CrmAnalytics.Application.Abstractions.Integrations;
using CrmAnalytics.Application.Analytics;
using CrmAnalytics.Application.Conversations;
using CrmAnalytics.Application.ReportProcessing;
using CrmAnalytics.Application.ReportRequests;
using CrmAnalytics.Contracts.Integrations;
using CrmAnalytics.Domain.ReportRequests;
using CrmAnalytics.Infrastructure.Persistence;

namespace CrmAnalytics.UnitTests;

public sealed class ReportProcessingServiceTests
{
    [Fact]
    public async Task ProcessAsync_EmptyScopeStopsBeforeRepositoryAndClients()
    {
        var fixture = new ProcessingFixture();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Service.ProcessAsync(
                new ProcessReportRequestCommand(
                    "missing",
                    UserDataScope.Empty),
                CancellationToken.None));

        Assert.Equal(0, fixture.Query.CallCount);
        Assert.Equal(0, fixture.Analytics.CallCount);
        Assert.Equal(0, fixture.Report.CallCount);
    }

    [Fact]
    public async Task ProcessAsync_CompletedFlow_MapsContextAndPersistsResult()
    {
        var fixture = new ProcessingFixture();
        var previous = fixture.CreateRequest();
        previous.TransitionTo(
            ReportRequestStatus.Validating,
            previous.UpdatedAt);
        previous.TransitionTo(
            ReportRequestStatus.Processing,
            previous.UpdatedAt);
        previous.Complete(
            "previous-report",
            "Previous summary.",
            "https://app.powerbi.com/reports/previous",
            previous.UpdatedAt);
        await fixture.Repository.AddAsync(previous, CancellationToken.None);
        var current = fixture.CreateRequest(
            previousRequestId: previous.RequestId);
        await fixture.Repository.AddAsync(current, CancellationToken.None);
        var scope = TestDataScopeFactory.Create(roles: ["Sales"]);

        var result = await fixture.Service.ProcessAsync(
            new ProcessReportRequestCommand(current.RequestId, scope),
            CancellationToken.None);

        Assert.Equal(ReportRequestStatus.Completed, result.Status);
        Assert.Equal(["query", "analytics", "report"], fixture.CallOrder);
        Assert.Equal(1, fixture.Query.CallCount);
        Assert.Equal(1, fixture.Analytics.CallCount);
        Assert.Equal(1, fixture.Report.CallCount);
        Assert.Equal(current.RequestId, fixture.Query.Request?.RequestId);
        Assert.Equal(current.Prompt, fixture.Query.Request?.Prompt);
        Assert.NotSame(scope, fixture.Query.Request?.UserDataScope);
        Assert.True(
            fixture.Query.Request?.UserDataScope.AllowAllRegions);
        Assert.True(
            fixture.Query.Request?.UserDataScope.AllowAllStores);
        Assert.Equal(
            "Previous summary.",
            fixture.Query.Request?.ConversationContext.PreviousSummary);
        Assert.Equal(
            "https://app.powerbi.com/reports/previous",
            fixture.Query.Request?.ConversationContext.PreviousPowerBiUrl);
        Assert.Equal(
            "query-reference",
            fixture.Analytics.Request?.Request.QueryResultReference);
        Assert.Equal(
            "crm-analysis",
            fixture.Analytics.Request?.Request.AnalysisType);
        Assert.NotNull(fixture.Analytics.Request?.Request.Parameters);
        Assert.Empty(fixture.Analytics.Request!.Request.Parameters);
        Assert.Equal(
            "analytics-reference",
            fixture.Report.Request?.ResultReference);
        Assert.Equal("power-bi", fixture.Report.Request?.ReportType);
        Assert.Null(fixture.Report.Request?.UserDataScope);
        Assert.Equal("report-1", current.ReportId);
        Assert.Equal("Analytics summary.", current.Summary);
        Assert.Equal(
            "https://app.powerbi.com/reports/report-1",
            current.PowerBiUrl);
        Assert.Null(current.ClarificationQuestion);
        Assert.Null(result.ClarificationQuestion);
    }

    [Fact]
    public async Task ProcessAsync_Clarification_StoresQuestionAndStops()
    {
        var fixture = new ProcessingFixture();
        var request = await fixture.AddReceivedRequestAsync();
        fixture.Query.Response = new QueryPlanningResponse(
            ExternalOperationStatus.WaitingForClarification,
            null,
            "  Hangi tarih aralığı kullanılmalı?  ",
            null,
            null);

        var result = await fixture.ProcessAsync(request);

        Assert.Equal(
            ReportRequestStatus.WaitingForClarification,
            result.Status);
        Assert.Equal(
            "Hangi tarih aralığı kullanılmalı?",
            result.ClarificationQuestion);
        Assert.Equal(
            result.ClarificationQuestion,
            request.ClarificationQuestion);
        Assert.Equal(0, fixture.Analytics.CallCount);
        Assert.Equal(0, fixture.Report.CallCount);
    }

    [Fact]
    public async Task ProcessAsync_QueryFailure_FailsAndStops()
    {
        var fixture = new ProcessingFixture();
        var request = await fixture.AddReceivedRequestAsync();
        fixture.Query.Response = FailedQueryResponse(
            "QUERY_SERVICE_FAILED",
            "Query failed safely.");

        var result = await fixture.ProcessAsync(request);

        Assert.Equal(ReportRequestStatus.Failed, result.Status);
        Assert.Equal("QUERY_SERVICE_FAILED", result.ErrorCode);
        Assert.Equal("Query failed safely.", request.ErrorMessage);
        Assert.Equal(0, fixture.Analytics.CallCount);
        Assert.Equal(0, fixture.Report.CallCount);
    }

    [Fact]
    public async Task ProcessAsync_AnalyticsFailure_FailsAndSkipsReport()
    {
        var fixture = new ProcessingFixture();
        var request = await fixture.AddReceivedRequestAsync();
        fixture.Analytics.Response = new AnalyticsResponse(
            "job-1",
            ExternalOperationStatus.Failed,
            null,
            null,
            new ExternalServiceError(
                "ANALYTICS_SERVICE_FAILED",
                "Analytics failed safely.",
                false));

        var result = await fixture.ProcessAsync(request);

        Assert.Equal(ReportRequestStatus.Failed, result.Status);
        Assert.Equal("ANALYTICS_SERVICE_FAILED", result.ErrorCode);
        Assert.Equal(0, fixture.Report.CallCount);
    }

    [Fact]
    public async Task ProcessAsync_ReportFailure_Fails()
    {
        var fixture = new ProcessingFixture();
        var request = await fixture.AddReceivedRequestAsync();
        fixture.Report.Response = new ReportGenerationResponse(
            null,
            null,
            null,
            ExternalOperationStatus.Failed,
            new ExternalServiceError(
                "REPORT_SERVICE_FAILED",
                "Report failed safely.",
                false));

        var result = await fixture.ProcessAsync(request);

        Assert.Equal(ReportRequestStatus.Failed, result.Status);
        Assert.Equal("REPORT_SERVICE_FAILED", result.ErrorCode);
    }

    [Fact]
    public async Task ProcessAsync_UnsafeQueryError_UsesFallbacks()
    {
        var fixture = new ProcessingFixture();
        var request = await fixture.AddReceivedRequestAsync();
        fixture.Query.Response = FailedQueryResponse(
            "unsafe-code",
            " ");

        var result = await fixture.ProcessAsync(request);

        Assert.Equal("QUERY_PLANNING_FAILED", result.ErrorCode);
        Assert.Equal(
            "Rapor talebi sorgu planlama aşamasında tamamlanamadı.",
            request.ErrorMessage);
    }

    [Fact]
    public async Task ProcessAsync_CompletedQueryWithoutReference_Fails()
    {
        var fixture = new ProcessingFixture();
        var request = await fixture.AddReceivedRequestAsync();
        fixture.Query.Response = fixture.Query.Response with
        {
            GeneratedQueryReference = " "
        };

        var result = await fixture.ProcessAsync(request);

        Assert.Equal(ReportRequestStatus.Failed, result.Status);
        Assert.Equal("INVALID_QUERY_PLANNING_RESPONSE", result.ErrorCode);
        Assert.Equal(0, fixture.Analytics.CallCount);
        Assert.Equal(0, fixture.Report.CallCount);
    }

    [Fact]
    public async Task ProcessAsync_CompletedAnalyticsWithoutReference_Fails()
    {
        var fixture = new ProcessingFixture();
        var request = await fixture.AddReceivedRequestAsync();
        fixture.Analytics.Response = fixture.Analytics.Response with
        {
            ResultReference = null
        };

        var result = await fixture.ProcessAsync(request);

        Assert.Equal(ReportRequestStatus.Failed, result.Status);
        Assert.Equal("INVALID_ANALYTICS_RESPONSE", result.ErrorCode);
        Assert.Equal(0, fixture.Report.CallCount);
    }

    [Fact]
    public async Task ProcessAsync_CompletedReportWithoutId_Fails()
    {
        var fixture = new ProcessingFixture();
        var request = await fixture.AddReceivedRequestAsync();
        fixture.Report.Response = fixture.Report.Response with
        {
            ReportId = ""
        };

        var result = await fixture.ProcessAsync(request);

        Assert.Equal(ReportRequestStatus.Failed, result.Status);
        Assert.Equal(
            "INVALID_REPORT_GENERATION_RESPONSE",
            result.ErrorCode);
    }

    [Fact]
    public async Task ProcessAsync_InvalidPowerBiUrl_PropagatesDomainError()
    {
        var fixture = new ProcessingFixture();
        var request = await fixture.AddReceivedRequestAsync();
        fixture.Report.Response = fixture.Report.Response with
        {
            PowerBiUrl = "http://powerbi.example/report-1"
        };

        await Assert.ThrowsAsync<ArgumentException>(
            () => fixture.ProcessAsync(request));

        Assert.Equal(ReportRequestStatus.Processing, request.Status);
        Assert.Null(request.ErrorCode);
    }

    [Fact]
    public async Task ProcessAsync_MissingRequest_Throws()
    {
        var fixture = new ProcessingFixture();

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => fixture.Service.ProcessAsync(
                new ProcessReportRequestCommand(
                    "missing",
                    TestDataScopeFactory.Create()),
                CancellationToken.None));
    }

    [Fact]
    public async Task ProcessAsync_NonReceivedRequest_Throws()
    {
        var fixture = new ProcessingFixture();
        var request = await fixture.AddReceivedRequestAsync();
        request.TransitionTo(
            ReportRequestStatus.Validating,
            request.UpdatedAt);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.ProcessAsync(request));

        Assert.Equal(0, fixture.Query.CallCount);
    }

    [Fact]
    public async Task ProcessAsync_PreCanceledToken_DoesNotStart()
    {
        var fixture = new ProcessingFixture();
        var request = await fixture.AddReceivedRequestAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.Service.ProcessAsync(
                new ProcessReportRequestCommand(
                    request.RequestId,
                    TestDataScopeFactory.Create()),
                cancellation.Token));

        Assert.Equal(ReportRequestStatus.Received, request.Status);
        Assert.Equal(0, fixture.Query.CallCount);
    }

    [Fact]
    public async Task ProcessAsync_QueryCancellation_PropagatesAndStops()
    {
        var fixture = new ProcessingFixture();
        var request = await fixture.AddReceivedRequestAsync();
        using var cancellation = new CancellationTokenSource();
        fixture.Query.Handler = (_, token) =>
        {
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.FromResult(fixture.Query.Response);
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.Service.ProcessAsync(
                new ProcessReportRequestCommand(
                    request.RequestId,
                    TestDataScopeFactory.Create()),
                cancellation.Token));

        Assert.Equal(ReportRequestStatus.Validating, request.Status);
        Assert.Null(request.ErrorCode);
        Assert.Equal(0, fixture.Analytics.CallCount);
        Assert.Equal(0, fixture.Report.CallCount);
    }

    [Fact]
    public async Task ProcessAsync_ExternalTimeout_FailsSafely()
    {
        var fixture = new ProcessingFixture();
        var request = await fixture.AddReceivedRequestAsync();
        fixture.Query.Handler = (_, _) =>
            throw new TimeoutException("technical details");

        var result = await fixture.ProcessAsync(request);

        Assert.Equal(ReportRequestStatus.Failed, result.Status);
        Assert.Equal("EXTERNAL_SERVICE_TIMEOUT", result.ErrorCode);
        Assert.Equal(
            "Dış servis işlemi zaman aşımına uğradı.",
            request.ErrorMessage);
        Assert.DoesNotContain("technical", request.ErrorMessage);
    }

    [Fact]
    public async Task ProcessAsync_EmptyAnalyticsSummary_UsesFallback()
    {
        var fixture = new ProcessingFixture();
        var request = await fixture.AddReceivedRequestAsync();
        fixture.Analytics.Response = fixture.Analytics.Response with
        {
            Summary = " "
        };

        var result = await fixture.ProcessAsync(request);

        Assert.Equal(ReportRequestStatus.Completed, result.Status);
        Assert.Equal("Analiz başarıyla tamamlandı.", result.Summary);
    }

    [Theory]
    [InlineData(ExternalOperationStatus.Accepted)]
    [InlineData(ExternalOperationStatus.Running)]
    public async Task ProcessAsync_NonTerminalQueryStatus_Fails(
        ExternalOperationStatus status)
    {
        var fixture = new ProcessingFixture();
        var request = await fixture.AddReceivedRequestAsync();
        fixture.Query.Response = fixture.Query.Response with
        {
            Status = status
        };

        var result = await fixture.ProcessAsync(request);

        Assert.Equal("QUERY_PLANNING_NOT_COMPLETED", result.ErrorCode);
        Assert.Equal(0, fixture.Analytics.CallCount);
    }

    [Theory]
    [InlineData(
        "11111111-1111-4111-8111-111111111111",
        "22222222-2222-4222-8222-222222222222",
        true)]
    [InlineData(
        "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
        "22222222-2222-4222-8222-222222222222",
        false)]
    [InlineData(
        "11111111-1111-4111-8111-111111111111",
        "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb",
        false)]
    public async Task ProcessAsync_PreviousContextRequiresSameOwner(
        string currentUserId,
        string currentTenantId,
        bool contextExpected)
    {
        var fixture = new ProcessingFixture();
        var previous = fixture.CreateRequest(
            userId: "11111111-1111-4111-8111-111111111111",
            tenantId: "22222222-2222-4222-8222-222222222222");
        previous.TransitionTo(
            ReportRequestStatus.Validating,
            previous.UpdatedAt);
        previous.TransitionTo(
            ReportRequestStatus.Processing,
            previous.UpdatedAt);
        previous.Complete(
            "previous-report",
            "Previous safe summary.",
            "https://app.powerbi.com/reports/previous",
            previous.UpdatedAt);
        await fixture.Repository.AddAsync(
            previous,
            CancellationToken.None);
        var current = fixture.CreateRequest(
            previous.RequestId,
            currentUserId,
            currentTenantId);
        await fixture.Repository.AddAsync(current, CancellationToken.None);

        await fixture.ProcessAsync(current);

        Assert.Equal(
            contextExpected ? "Previous safe summary." : null,
            fixture.Query.Request?.ConversationContext.PreviousSummary);
        Assert.Equal(
            contextExpected
                ? "https://app.powerbi.com/reports/previous"
                : null,
            fixture.Query.Request?.ConversationContext.PreviousPowerBiUrl);
    }

    private static QueryPlanningResponse FailedQueryResponse(
        string code,
        string message)
    {
        return new QueryPlanningResponse(
            ExternalOperationStatus.Failed,
            null,
            null,
            null,
            new ExternalServiceError(code, message, false));
    }

    private sealed class ProcessingFixture
    {
        public ProcessingFixture()
        {
            var conversationRepository =
                new InMemoryConversationRepository();
            var contextService = new ConversationContextService(
                conversationRepository,
                Repository);
            var requestService = new ReportRequestService(
                Repository,
                contextService);

            Query = new RecordingQueryClient(CallOrder);
            Analytics = new RecordingAnalyticsClient(CallOrder);
            Report = new RecordingReportClient(CallOrder);
            Service = new ReportProcessingService(
                Repository,
                requestService,
                Query,
                Analytics,
                Report);
        }

        public InMemoryReportRequestRepository Repository { get; } = new();
        public List<string> CallOrder { get; } = [];
        public RecordingQueryClient Query { get; }
        public RecordingAnalyticsClient Analytics { get; }
        public RecordingReportClient Report { get; }
        public ReportProcessingService Service { get; }

        public ReportRequest CreateRequest(
            string? previousRequestId = null,
            string? userId = null,
            string? tenantId = null)
        {
            return ReportRequest.Create(
                Guid.NewGuid().ToString("N"),
                $"conversation-{Guid.NewGuid():N}",
                previousRequestId,
                "Show CRM sales.",
                Guid.NewGuid().ToString("N"),
                userId,
                tenantId);
        }

        public async Task<ReportRequest> AddReceivedRequestAsync()
        {
            var request = CreateRequest();
            await Repository.AddAsync(request, CancellationToken.None);
            return request;
        }

        public Task<ProcessReportRequestResult> ProcessAsync(
            ReportRequest request)
        {
            return Service.ProcessAsync(
                new ProcessReportRequestCommand(
                    request.RequestId,
                    TestDataScopeFactory.Create()),
                CancellationToken.None);
        }
    }

    private sealed class RecordingQueryClient : IQueryPlanningClient
    {
        private readonly List<string> _callOrder;

        public RecordingQueryClient(List<string> callOrder)
        {
            _callOrder = callOrder;
        }

        public int CallCount { get; private set; }
        public QueryPlanningRequest? Request { get; private set; }
        public QueryPlanningResponse Response { get; set; } = new(
            ExternalOperationStatus.Completed,
            "canonical",
            null,
            "query-reference",
            null);
        public Func<QueryPlanningRequest, CancellationToken,
            Task<QueryPlanningResponse>>? Handler { get; set; }

        public Task<QueryPlanningResponse> PlanAsync(
            QueryPlanningRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            Request = request;
            _callOrder.Add("query");
            return Handler is null
                ? Task.FromResult(Response)
                : Handler(request, cancellationToken);
        }
    }

    private sealed class RecordingAnalyticsClient : IAnalyticsClient
    {
        private readonly List<string> _callOrder;

        public RecordingAnalyticsClient(List<string> callOrder)
        {
            _callOrder = callOrder;
        }

        public int CallCount { get; private set; }
        public AnalyticsExecutionRequest? Request { get; private set; }
        public AnalyticsResponse Response { get; set; } = new(
            "job-1",
            ExternalOperationStatus.Completed,
            "analytics-reference",
            "Analytics summary.",
            null);

        public Task<AnalyticsResponse> AnalyzeAsync(
            AnalyticsExecutionRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            Request = request;
            _callOrder.Add("analytics");
            return Task.FromResult(Response);
        }
    }

    private sealed class RecordingReportClient : IReportClient
    {
        private readonly List<string> _callOrder;

        public RecordingReportClient(List<string> callOrder)
        {
            _callOrder = callOrder;
        }

        public int CallCount { get; private set; }
        public ReportGenerationRequest? Request { get; private set; }
        public ReportGenerationResponse Response { get; set; } = new(
            "report-1",
            "ReportSection",
            "https://app.powerbi.com/reports/report-1",
            ExternalOperationStatus.Completed,
            null);

        public Task<ReportGenerationResponse> GenerateAsync(
            ReportGenerationRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            Request = request;
            _callOrder.Add("report");
            return Task.FromResult(Response);
        }
    }
}
