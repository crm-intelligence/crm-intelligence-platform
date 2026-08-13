using CrmAnalytics.Application.Abstractions.Integrations;
using CrmAnalytics.Application.Analytics;
using CrmAnalytics.Application.Auditing;
using CrmAnalytics.Application.Conversations;
using CrmAnalytics.Application.ReportProcessing;
using CrmAnalytics.Application.ReportRequests;
using CrmAnalytics.Application.SqlProduction;
using CrmAnalytics.Contracts.Integrations;
using CrmAnalytics.Domain.ReportRequests;
using CrmAnalytics.Infrastructure.Integrations;
using CrmAnalytics.Infrastructure.Auditing;
using CrmAnalytics.Infrastructure.Persistence;

namespace CrmAnalytics.UnitTests;

public sealed class SqlProductionReportProcessingTests
{
    private static readonly string UserId =
        Guid.Parse("11111111-1111-1111-1111-111111111111")
            .ToString("D");
    private static readonly string TenantId =
        Guid.Parse("22222222-2222-2222-2222-222222222222")
            .ToString("D");

    [Fact]
    public async Task Accepted_ExecutesPlanAndContinuesToCompleted()
    {
        var canonical = "{\"decision\":\"accepted\"}";
        var sqlClient = new RecordingSqlProductionClient(
            Accepted(canonical));
        var executionResult = CreateExecutionResult(
            "query-result-1", SqlDataSource.Dwh, 3);
        var executionClient = new RecordingExecutionClient(executionResult);
        var harness = await CreateHarnessAsync(
            sqlClient,
            executionClient);

        var result = await harness.Service.ProcessAsync(
            new ProcessReportRequestCommand(
                harness.Request.RequestId,
                CreateScope(),
                AttemptNumber: 4,
                DeliveryId: "message-delivery-4"),
            CancellationToken.None);

        Assert.Equal(ReportRequestStatus.Completed, result.Status);
        Assert.Equal(canonical, harness.Request.CanonicalRequestJson);
        Assert.Equal(1, executionClient.CallCount);
        Assert.Same(
            sqlClient.Result.ExecutionPlan,
            executionClient.ExecutionPlan);
        Assert.Equal(1, harness.Analytics.CallCount);
        Assert.Equal("query-result-1", harness.Analytics.LastReference);
        Assert.Same(executionResult, harness.Analytics.QueryResult);
        Assert.Equal(1, harness.Report.CallCount);
        Assert.Equal(
            harness.Request.ReferenceDate,
            sqlClient.Request?.Today);
        Assert.Null(sqlClient.Request?.PreviousCanonicalRequestJson);
        Assert.Equal(SqlDataSource.Unknown, sqlClient.Request?.Source);
        var audit = Assert.Single(harness.Audit.Snapshot, value =>
            value.EventType
                == ApplicationAuditEventType.QueryExecutionSucceeded);
        Assert.Equal("Dwh", audit.DataSource);
        Assert.Equal(3, audit.RowCount);
        Assert.Equal(false, audit.ResultTruncated);
        Assert.Null(audit.ReasonCode);
        var started = Assert.Single(harness.Audit.Snapshot, value =>
            value.EventType
                == ApplicationAuditEventType.QueryExecutionStarted);
        Assert.Equal(4, started.AuditMetadata!.AttemptNumber);
        Assert.Equal("message-delivery-4",
            started.AuditMetadata.DeliveryId);
        Assert.Equal("conversation-1",
            started.AuditMetadata.ConversationId);
        Assert.Equal(ApplicationAuditMetadata.DwhContractVersion,
            started.AuditMetadata.SqlContractVersion);
        Assert.Equal(started.AuditMetadata, audit.AuditMetadata);
    }

    [Fact]
    public async Task OltpPlan_IsPreservedThroughExecutionAndAnalytics()
    {
        var executionResult = CreateExecutionResult(
            "oltp-result", SqlDataSource.Oltp, 1);
        var execution = new RecordingExecutionClient(executionResult);
        var harness = await CreateHarnessAsync(
            new RecordingSqlProductionClient(
                Accepted(null, SqlDataSource.Oltp)),
            execution);

        var result = await harness.Service.ProcessAsync(
            new ProcessReportRequestCommand(
                harness.Request.RequestId,
                CreateScope()),
            CancellationToken.None);

        Assert.Equal(ReportRequestStatus.Completed, result.Status);
        Assert.Equal(SqlDataSource.Oltp, execution.ExecutionPlan?.Source);
        Assert.Same(executionResult, harness.Analytics.QueryResult);
        var oltpAudit = Assert.Single(harness.Audit.Snapshot, value =>
            value.EventType
                == ApplicationAuditEventType.QueryExecutionSucceeded);
        Assert.Equal("Oltp", oltpAudit.DataSource);
        Assert.Equal(ApplicationAuditMetadata.OltpContractVersion,
            oltpAudit.AuditMetadata!.SqlContractVersion);
        Assert.Equal(15, oltpAudit.AuditMetadata.TimeoutSeconds);
        Assert.Equal(1000, oltpAudit.AuditMetadata.RowLimit);
    }

    [Fact]
    public async Task QueryLimitFailure_SkipsAnalyticsAndReportAndWritesSafeAudit()
    {
        var execution = new RecordingExecutionClient(
            CreateExecutionResult("unused", SqlDataSource.Dwh, 0))
        {
            Exception = new QueryResultLimitExceededException(
                SqlDataSource.Dwh,
                TimeSpan.FromMilliseconds(12))
        };
        var harness = await CreateHarnessAsync(
            new RecordingSqlProductionClient(Accepted(null)),
            execution);

        var result = await harness.Service.ProcessAsync(
            new ProcessReportRequestCommand(
                harness.Request.RequestId,
                CreateScope()),
            CancellationToken.None);

        Assert.Equal(ReportRequestStatus.Failed, result.Status);
        Assert.Equal(QueryExecutionErrorCodes.ResultLimitExceeded,
            result.ErrorCode);
        Assert.Equal(QueryExecutionMessages.ResultLimit,
            harness.Request.ErrorMessage);
        Assert.Equal(0, harness.Analytics.CallCount);
        Assert.Equal(0, harness.Report.CallCount);
        var audit = Assert.Single(harness.Audit.Snapshot, value =>
            value.EventType
                == ApplicationAuditEventType.QueryExecutionFailed);
        Assert.Equal(QueryExecutionErrorCodes.ResultLimitExceeded,
            audit.ReasonCode);
        Assert.Equal(12, audit.DurationMilliseconds);
        Assert.Null(audit.RowCount);
    }

    [Fact]
    public async Task NeedsClarification_StoresCanonicalAndSkipsDownstream()
    {
        var sqlClient = new RecordingSqlProductionClient(
            new SqlProductionClientResult(
                SqlProductionClientDecision.NeedsClarification,
                "{\"decision\":\"needs_clarification\"}",
                "Hangi dönem?",
                null,
                null,
                null));
        var executionClient = new RecordingExecutionClient(
            CreateExecutionResult("must-not-run", SqlDataSource.Dwh, 0));
        var harness = await CreateHarnessAsync(
            sqlClient,
            executionClient);

        var result = await harness.Service.ProcessAsync(
            new ProcessReportRequestCommand(
                harness.Request.RequestId,
                CreateScope()),
            CancellationToken.None);

        Assert.Equal(
            ReportRequestStatus.WaitingForClarification,
            result.Status);
        Assert.Equal("Hangi dönem?", result.ClarificationQuestion);
        Assert.Equal(
            "{\"decision\":\"needs_clarification\"}",
            harness.Request.CanonicalRequestJson);
        Assert.Equal(0, executionClient.CallCount);
        Assert.Equal(0, harness.Analytics.CallCount);
        Assert.Equal(0, harness.Report.CallCount);
    }

    [Theory]
    [InlineData("GR007")]
    [InlineData("GR014")]
    public async Task Rejected_IsControlledTerminalAndSkipsDownstream(
        string reasonCode)
    {
        var sqlClient = new RecordingSqlProductionClient(
            new SqlProductionClientResult(
                SqlProductionClientDecision.Rejected,
                "{\"decision\":\"rejected\"}",
                "Bu talep güvenlik politikası nedeniyle işlenemedi.",
                reasonCode,
                null,
                null));
        var executionClient = new RecordingExecutionClient(
            CreateExecutionResult("must-not-run", SqlDataSource.Dwh, 0));
        var harness = await CreateHarnessAsync(
            sqlClient,
            executionClient);

        var result = await harness.Service.ProcessAsync(
            new ProcessReportRequestCommand(
                harness.Request.RequestId,
                CreateScope()),
            CancellationToken.None);

        Assert.Equal(ReportRequestStatus.Rejected, result.Status);
        Assert.Equal(reasonCode, result.RejectionCode);
        Assert.Equal(
            "Bu talep güvenlik politikası nedeniyle işlenemedi.",
            result.RejectionMessage);
        Assert.Null(harness.Request.ErrorCode);
        Assert.Null(harness.Request.ErrorMessage);
        Assert.Equal(
            "{\"decision\":\"rejected\"}",
            harness.Request.CanonicalRequestJson);
        Assert.Equal(0, executionClient.CallCount);
        Assert.Equal(0, harness.Analytics.CallCount);
        Assert.Equal(0, harness.Report.CallCount);
    }

    [Fact]
    public async Task AcceptedWithoutConfiguredExecutor_FailsTechnically()
    {
        var harness = await CreateHarnessAsync(
            new RecordingSqlProductionClient(Accepted(null)),
            new UnconfiguredQueryExecutionClient());

        var result = await harness.Service.ProcessAsync(
            new ProcessReportRequestCommand(
                harness.Request.RequestId,
                CreateScope()),
            CancellationToken.None);

        Assert.Equal(ReportRequestStatus.Failed, result.Status);
        Assert.Equal("QUERY_EXECUTION_FAILED", result.ErrorCode);
        Assert.Equal(0, harness.Analytics.CallCount);
        Assert.Equal(0, harness.Report.CallCount);
    }

    [Fact]
    public async Task InvalidAcceptedResponseFailsBeforeExecution()
    {
        var executionClient = new RecordingExecutionClient(
            CreateExecutionResult("must-not-run", SqlDataSource.Dwh, 0));
        var harness = await CreateHarnessAsync(
            new RecordingSqlProductionClient(
                new SqlProductionClientResult(
                    SqlProductionClientDecision.Accepted,
                    null,
                    null,
                    null,
                    null,
                    null)),
            executionClient);

        var result = await harness.Service.ProcessAsync(
            new ProcessReportRequestCommand(
                harness.Request.RequestId,
                CreateScope()),
            CancellationToken.None);

        Assert.Equal(ReportRequestStatus.Failed, result.Status);
        Assert.Equal(
            "INVALID_SQL_PRODUCTION_RESPONSE",
            result.ErrorCode);
        Assert.Equal(0, executionClient.CallCount);
    }

    [Fact]
    public async Task CancellationIsNotConvertedToFailedOrRejected()
    {
        var sqlClient = new RecordingSqlProductionClient(
            Accepted(null))
        {
            Exception = new OperationCanceledException()
        };
        var harness = await CreateHarnessAsync(
            sqlClient,
            new RecordingExecutionClient(
                CreateExecutionResult("unused", SqlDataSource.Dwh, 0)));

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => harness.Service.ProcessAsync(
                new ProcessReportRequestCommand(
                    harness.Request.RequestId,
                    CreateScope()),
                CancellationToken.None));

        Assert.Equal(
            ReportRequestStatus.Validating,
            harness.Request.Status);
        Assert.Null(harness.Request.ErrorCode);
        Assert.Null(harness.Request.RejectionCode);
    }

    private static SqlProductionClientResult Accepted(
        string? canonical,
        SqlDataSource source = SqlDataSource.Dwh) =>
        new(
            SqlProductionClientDecision.Accepted,
            canonical,
            null,
            null,
            new SqlExecutionPlan(
                source,
                "SELECT * FROM vw_sales WHERE customer_state = @region",
                [
                    new SqlExecutionParameter(
                        "@region",
                        SqlExecutionParameterKind.Text,
                        "SP",
                        true)
                ],
                "customer_state = @region",
                source == SqlDataSource.Oltp ? 15 : 30,
                null,
                source == SqlDataSource.Oltp
                    ? "dbo.vw_operational_orders"
                    : "mart.vw_sales",
                source == SqlDataSource.Oltp ? 1000 : 5000),
            null);

    private static async Task<Harness> CreateHarnessAsync(
        RecordingSqlProductionClient sqlClient,
        IQueryExecutionClient executionClient)
    {
        var repository = new InMemoryReportRequestRepository();
        var request = ReportRequest.Create(
            Guid.NewGuid().ToString("N"),
            "conversation-1",
            null,
            "2018 satış raporu",
            "correlation-1",
            UserId,
            TenantId);
        await repository.AddAsync(request, CancellationToken.None);
        var conversationService = new ConversationContextService(
            new InMemoryConversationRepository(),
            repository);
        var requestService = new ReportRequestService(
            repository,
            conversationService);
        var analytics = new RecordingAnalyticsClient();
        var report = new RecordingReportClient();
        var audit = new InMemoryApplicationAuditWriter();
        var service = new ReportProcessingService(
            repository,
            requestService,
            new ThrowingQueryPlanningClient(),
            analytics,
            report,
            sqlClient,
            executionClient,
            audit);
        return new Harness(
            service,
            request,
            analytics,
            report,
            audit);
    }

    private static UserDataScope CreateScope() =>
        new()
        {
            UserId = UserId,
            TenantId = TenantId,
            AllowAllRegions = false,
            AllowAllStores = true,
            AllowedRegions = ["SP"]
        };

    private sealed record Harness(
        ReportProcessingService Service,
        ReportRequest Request,
        RecordingAnalyticsClient Analytics,
        RecordingReportClient Report,
        InMemoryApplicationAuditWriter Audit);

    private sealed class RecordingSqlProductionClient
        : ISqlProductionClient
    {
        public RecordingSqlProductionClient(
            SqlProductionClientResult result)
        {
            Result = result;
        }

        public SqlProductionClientResult Result { get; }

        public Exception? Exception { get; init; }

        public SqlProductionClientRequest? Request { get; private set; }

        public Task<SqlProductionClientResult> ProduceAsync(
            SqlProductionClientRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Request = request;
            if (Exception is not null)
            {
                throw Exception;
            }

            return Task.FromResult(Result);
        }
    }

    private static QueryExecutionResult CreateExecutionResult(
        string reference,
        SqlDataSource source,
        int rowCount) => new(
            reference,
            source,
            Array.Empty<QueryResultColumn>(),
            Enumerable.Range(0, rowCount)
                .Select(_ => new QueryResultRow(
                    Array.Empty<QueryResultValue>()))
                .ToArray(),
            false,
            0,
            DateTimeOffset.UtcNow,
            TimeSpan.Zero);

    private sealed class RecordingExecutionClient : IQueryExecutionClient
    {
        private readonly QueryExecutionResult _result;

        public RecordingExecutionClient(QueryExecutionResult result)
        {
            _result = result;
        }

        public int CallCount { get; private set; }

        public SqlExecutionPlan? ExecutionPlan { get; private set; }

        public Exception? Exception { get; init; }

        public Task<QueryExecutionResult> ExecuteAsync(
            SqlExecutionPlan executionPlan,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            ExecutionPlan = executionPlan;
            if (Exception is not null)
            {
                throw Exception;
            }
            return Task.FromResult(_result);
        }
    }

    private sealed class RecordingAnalyticsClient : IAnalyticsClient
    {
        public int CallCount { get; private set; }

        public string? LastReference { get; private set; }

        public QueryExecutionResult? QueryResult { get; private set; }

        public Task<AnalyticsResponse> AnalyzeAsync(
            AnalyticsExecutionRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            LastReference = request.Request.QueryResultReference;
            QueryResult = request.QueryResult;
            return Task.FromResult(new AnalyticsResponse(
                "job-1",
                ExternalOperationStatus.Completed,
                "analysis-result-1",
                "Güvenli özet",
                null));
        }
    }

    private sealed class RecordingReportClient : IReportClient
    {
        public int CallCount { get; private set; }

        public Task<ReportGenerationResponse> GenerateAsync(
            ReportGenerationRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return Task.FromResult(new ReportGenerationResponse(
                "report-1",
                "Report",
                "https://app.powerbi.com/report/1",
                ExternalOperationStatus.Completed,
                null));
        }
    }

    private sealed class ThrowingQueryPlanningClient
        : IQueryPlanningClient
    {
        public Task<QueryPlanningResponse> PlanAsync(
            QueryPlanningRequest request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                "The legacy planning client must not be called.");
    }
}
