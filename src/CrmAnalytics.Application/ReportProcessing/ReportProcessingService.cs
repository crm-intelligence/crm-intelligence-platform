using CrmAnalytics.Application.Abstractions.Integrations;
using CrmAnalytics.Application.Abstractions.Persistence;
using CrmAnalytics.Application.Analytics;
using CrmAnalytics.Application.Auditing;
using CrmAnalytics.Application.Exceptions;
using CrmAnalytics.Application.Identity;
using CrmAnalytics.Application.ReportRequests;
using CrmAnalytics.Application.SqlProduction;
using CrmAnalytics.Contracts.Integrations;
using CrmAnalytics.Domain.ReportRequests;

namespace CrmAnalytics.Application.ReportProcessing;

public sealed class ReportProcessingService : IReportProcessingService
{
    private const string QueryPlanningFailedCode =
        "QUERY_PLANNING_FAILED";
    private const string QueryPlanningFailedMessage =
        "Rapor talebi sorgu planlama aşamasında tamamlanamadı.";
    private const string InvalidQueryPlanningResponseCode =
        "INVALID_QUERY_PLANNING_RESPONSE";
    private const string InvalidQueryPlanningResponseMessage =
        "Sorgu planlama servisinden geçerli sonuç alınamadı.";
    private const string QueryPlanningNotCompletedCode =
        "QUERY_PLANNING_NOT_COMPLETED";
    private const string AnalyticsFailedCode = "ANALYTICS_FAILED";
    private const string AnalyticsFailedMessage =
        "Analitik işlem tamamlanamadı.";
    private const string InvalidAnalyticsResponseCode =
        "INVALID_ANALYTICS_RESPONSE";
    private const string InvalidAnalyticsResponseMessage =
        "Analitik servisten geçerli sonuç alınamadı.";
    private const string AnalyticsNotCompletedCode =
        "ANALYTICS_NOT_COMPLETED";
    private const string ReportGenerationFailedCode =
        "REPORT_GENERATION_FAILED";
    private const string ReportGenerationFailedMessage =
        "Rapor bağlantısı oluşturulamadı.";
    private const string InvalidReportGenerationResponseCode =
        "INVALID_REPORT_GENERATION_RESPONSE";
    private const string InvalidReportGenerationResponseMessage =
        "Rapor oluşturma servisinden geçerli sonuç alınamadı.";
    private const string ReportGenerationNotCompletedCode =
        "REPORT_GENERATION_NOT_COMPLETED";
    private const string TimeoutCode = "EXTERNAL_SERVICE_TIMEOUT";
    private const string TimeoutMessage =
        "Dış servis işlemi zaman aşımına uğradı.";
    private const string DefaultAnalyticsSummary =
        "Analiz başarıyla tamamlandı.";
    private const string InvalidSqlProductionResponseCode =
        "INVALID_SQL_PRODUCTION_RESPONSE";
    private const string InvalidSqlProductionResponseMessage =
        "SQL üretim servisinden geçerli sonuç alınamadı.";
    private const string SqlProductionFailedCode =
        "SQL_PRODUCTION_FAILED";
    private const string SqlProductionFailedMessage =
        "SQL üretim işlemi teknik bir hata nedeniyle tamamlanamadı.";
    private const string QueryExecutionFailedCode =
        "QUERY_EXECUTION_FAILED";
    private const string QueryExecutionFailedMessage =
        QueryExecutionMessages.General;
    private const string ClarificationFallback =
        "Rapor talebinizi netleştirmek için ek bilgi gerekiyor.";
    private const string RejectionFallback =
        "Rapor talebi güvenlik veya sorgu politikaları nedeniyle "
        + "işlenemedi.";

    private readonly IReportRequestRepository _repository;
    private readonly IReportRequestService _reportRequestService;
    private readonly IQueryPlanningClient _queryPlanningClient;
    private readonly IAnalyticsClient _analyticsClient;
    private readonly IReportClient _reportClient;
    private readonly ISqlProductionClient? _sqlProductionClient;
    private readonly IQueryExecutionClient? _queryExecutionClient;
    private readonly IApplicationAuditWriter? _auditWriter;
    private readonly IPowerBiReportRouter? _powerBiReportRouter;

    public ReportProcessingService(
        IReportRequestRepository repository,
        IReportRequestService reportRequestService,
        IQueryPlanningClient queryPlanningClient,
        IAnalyticsClient analyticsClient,
        IReportClient reportClient,
        ISqlProductionClient? sqlProductionClient = null,
        IQueryExecutionClient? queryExecutionClient = null,
        IApplicationAuditWriter? auditWriter = null,
        IPowerBiReportRouter? powerBiReportRouter = null)
    {
        _repository = repository;
        _reportRequestService = reportRequestService;
        _queryPlanningClient = queryPlanningClient;
        _analyticsClient = analyticsClient;
        _reportClient = reportClient;
        _sqlProductionClient = sqlProductionClient;
        _queryExecutionClient = queryExecutionClient;
        _auditWriter = auditWriter;
        _powerBiReportRouter = powerBiReportRouter;
    }

    public async Task<ProcessReportRequestResult> ProcessAsync(
        ProcessReportRequestCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.UserDataScope);
        cancellationToken.ThrowIfCancellationRequested();
        if (command.AttemptNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(command.AttemptNumber));
        }

        UserDataScope validatedScope;
        try
        {
            validatedScope =
                UserDataScopeValidator.CreateRequiredSnapshot(
                    command.UserDataScope);
        }
        catch (ForbiddenAccessException exception)
        {
            throw new InvalidOperationException(
                "Report processing requires a valid data scope.",
                exception);
        }

        var reportRequest = await _repository.GetByIdAsync(
            command.RequestId,
            cancellationToken);

        if (reportRequest is null)
        {
            throw new KeyNotFoundException(
                $"A report request with ID '{command.RequestId}' was not found.");
        }

        var isClarificationResume =
            reportRequest.Status
                == ReportRequestStatus.WaitingForClarification;

        if (isClarificationResume
            && string.IsNullOrWhiteSpace(
                reportRequest.ClarificationResponse))
        {
            throw new InvalidOperationException(
                "A clarification response is required to resume "
                + "processing.");
        }

        if (reportRequest.Status != ReportRequestStatus.Received
            && !isClarificationResume)
        {
            throw new InvalidOperationException(
                "Only received report requests or requests with a "
                + "clarification response can be processed.");
        }

        await _reportRequestService.TransitionStatusAsync(
            new TransitionReportRequestStatusCommand(
                RequestId: reportRequest.RequestId,
                TargetStatus: ReportRequestStatus.Validating,
                TransitionedAt: GetCurrentUtcTime(reportRequest)),
            cancellationToken);

        reportRequest = await GetRequiredAsync(
            reportRequest.RequestId,
            cancellationToken);

        if (_sqlProductionClient is not null)
        {
            var sqlPreparation = await PrepareSqlQueryAsync(
                reportRequest,
                validatedScope,
                command.AttemptNumber,
                command.DeliveryId,
                command.SemanticPlan,
                cancellationToken);

            if (sqlPreparation.TerminalResult is not null)
            {
                return sqlPreparation.TerminalResult;
            }

            return await ContinueWithAnalyticsAsync(
                reportRequest,
                sqlPreparation.ResultReference!,
                sqlPreparation.QueryResult,
                sqlPreparation.ResultShape,
                command.SemanticPlan,
                cancellationToken);
        }

        var conversationContext = await LoadConversationContextAsync(
            reportRequest,
            cancellationToken);
        var queryPlanningRequest = new QueryPlanningRequest(
            RequestId: reportRequest.RequestId,
            Prompt: reportRequest.Prompt,
            ClarificationResponse:
                reportRequest.ClarificationResponse,
            PreviousRequestId: reportRequest.PreviousRequestId,
            UserDataScope: validatedScope,
            ConversationContext: conversationContext);

        QueryPlanningResponse queryPlanningResponse;

        try
        {
            queryPlanningResponse = await _queryPlanningClient.PlanAsync(
                queryPlanningRequest,
                cancellationToken);
        }
        catch (TimeoutException)
        {
            return await FailAsync(
                reportRequest,
                TimeoutCode,
                TimeoutMessage,
                cancellationToken);
        }

        if (queryPlanningResponse.Status
            == ExternalOperationStatus.WaitingForClarification)
        {
            if (string.IsNullOrWhiteSpace(
                    queryPlanningResponse.ClarificationQuestion))
            {
                return await FailAsync(
                    reportRequest,
                    InvalidQueryPlanningResponseCode,
                    InvalidQueryPlanningResponseMessage,
                    cancellationToken);
            }

            await _reportRequestService.RequestClarificationAsync(
                new RequestReportClarificationCommand(
                    RequestId: reportRequest.RequestId,
                    ClarificationQuestion:
                        queryPlanningResponse.ClarificationQuestion,
                    RequestedAt: GetCurrentUtcTime(reportRequest)),
                cancellationToken);

            return CreateResult(await GetRequiredAsync(
                reportRequest.RequestId,
                cancellationToken));
        }

        if (queryPlanningResponse.Status == ExternalOperationStatus.Failed)
        {
            return await FailAsync(
                reportRequest,
                GetSafeErrorCode(
                    queryPlanningResponse.Error,
                    QueryPlanningFailedCode),
                GetSafeErrorMessage(
                    queryPlanningResponse.Error,
                    QueryPlanningFailedMessage),
                cancellationToken);
        }

        if (queryPlanningResponse.Status
            != ExternalOperationStatus.Completed)
        {
            return await FailAsync(
                reportRequest,
                QueryPlanningNotCompletedCode,
                GetSafeErrorMessage(
                    queryPlanningResponse.Error,
                    QueryPlanningFailedMessage),
                cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(
                queryPlanningResponse.GeneratedQueryReference))
        {
            return await FailAsync(
                reportRequest,
                InvalidQueryPlanningResponseCode,
                InvalidQueryPlanningResponseMessage,
                cancellationToken);
        }
        return await ContinueWithAnalyticsAsync(
            reportRequest,
            queryPlanningResponse.GeneratedQueryReference,
            null,
            null,
            null,
            cancellationToken);
    }

    private async Task<ProcessReportRequestResult>
        ContinueWithAnalyticsAsync(
            ReportRequest reportRequest,
            string queryResultReference,
            QueryExecutionResult? queryResult,
            SqlResultShapeMetadata? resultShape,
            SubmittedSemanticPlanningResult? semanticPlan,
            CancellationToken cancellationToken)
    {

        await _reportRequestService.TransitionStatusAsync(
            new TransitionReportRequestStatusCommand(
                RequestId: reportRequest.RequestId,
                TargetStatus: ReportRequestStatus.Processing,
                TransitionedAt: GetCurrentUtcTime(reportRequest)),
            cancellationToken);

        reportRequest = await GetRequiredAsync(
            reportRequest.RequestId,
            cancellationToken);
        var analyticsRequest = new AnalyticsExecutionRequest(
            new AnalyticsRequest(
                RequestId: reportRequest.RequestId,
                AnalysisType: ReportProcessingConstants.AnalysisType,
                QueryResultReference: queryResultReference,
                Parameters: new Dictionary<string, string?>()),
            queryResult);
        AnalyticsResponse analyticsResponse;

        try
        {
            analyticsResponse = await _analyticsClient.AnalyzeAsync(
                analyticsRequest,
                cancellationToken);
        }
        catch (TimeoutException)
        {
            return await FailAsync(
                reportRequest,
                TimeoutCode,
                TimeoutMessage,
                cancellationToken);
        }

        if (analyticsResponse.Status == ExternalOperationStatus.Failed)
        {
            return await FailAsync(
                reportRequest,
                GetSafeErrorCode(
                    analyticsResponse.Error,
                    AnalyticsFailedCode),
                GetSafeErrorMessage(
                    analyticsResponse.Error,
                    AnalyticsFailedMessage),
                cancellationToken);
        }

        if (analyticsResponse.Status != ExternalOperationStatus.Completed)
        {
            return await FailAsync(
                reportRequest,
                AnalyticsNotCompletedCode,
                GetSafeErrorMessage(
                    analyticsResponse.Error,
                    AnalyticsFailedMessage),
                cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(analyticsResponse.ResultReference))
        {
            return await FailAsync(
                reportRequest,
                InvalidAnalyticsResponseCode,
                InvalidAnalyticsResponseMessage,
                cancellationToken);
        }

        var summary = string.IsNullOrWhiteSpace(analyticsResponse.Summary)
            ? DefaultAnalyticsSummary
            : analyticsResponse.Summary.Trim();
        var reportGenerationRequest = new ReportGenerationRequest(
            RequestId: reportRequest.RequestId,
            ReportType: ReportProcessingConstants.ReportType,
            ResultReference: analyticsResponse.ResultReference);
        ReportGenerationResponse reportGenerationResponse;

        try
        {
            reportGenerationResponse = await _reportClient.GenerateAsync(
                reportGenerationRequest,
                cancellationToken);
        }
        catch (TimeoutException)
        {
            return await FailAsync(
                reportRequest,
                TimeoutCode,
                TimeoutMessage,
                cancellationToken);
        }

        if (reportGenerationResponse.Status
            == ExternalOperationStatus.Failed)
        {
            return await FailAsync(
                reportRequest,
                GetSafeErrorCode(
                    reportGenerationResponse.Error,
                    ReportGenerationFailedCode),
                GetSafeErrorMessage(
                    reportGenerationResponse.Error,
                    ReportGenerationFailedMessage),
                cancellationToken);
        }

        if (reportGenerationResponse.Status
            != ExternalOperationStatus.Completed)
        {
            return await FailAsync(
                reportRequest,
                ReportGenerationNotCompletedCode,
                GetSafeErrorMessage(
                    reportGenerationResponse.Error,
                    ReportGenerationFailedMessage),
                cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(reportGenerationResponse.ReportId)
            || string.IsNullOrWhiteSpace(
                reportGenerationResponse.PowerBiUrl))
        {
            return await FailAsync(
                reportRequest,
                InvalidReportGenerationResponseCode,
                InvalidReportGenerationResponseMessage,
                cancellationToken);
        }

        var routedReport = queryResult is not null
            && _powerBiReportRouter is not null
                ? _powerBiReportRouter.Route(
                    queryResult.Source,
                    resultShape,
                    semanticPlan)
                : null;
        var visualizationPreview =
            ReportVisualizationPreviewFactory.Create(
                queryResult, resultShape);

        await _reportRequestService.CompleteAsync(
            new CompleteReportRequestCommand(
                RequestId: reportRequest.RequestId,
                ReportId: routedReport?.ReportId
                    ?? reportGenerationResponse.ReportId,
                Summary: summary,
                PowerBiUrl: routedReport?.Url
                    ?? reportGenerationResponse.PowerBiUrl,
                CompletedAt: GetCurrentUtcTime(reportRequest),
                VisualizationPreview: visualizationPreview),
            cancellationToken);

        return CreateResult(await GetRequiredAsync(
            reportRequest.RequestId,
            cancellationToken));
    }

    private async Task<SqlPreparationOutcome> PrepareSqlQueryAsync(
        ReportRequest reportRequest,
        UserDataScope userDataScope,
        int attemptNumber,
        string? deliveryId,
        SubmittedSemanticPlanningResult? semanticPlan,
        CancellationToken cancellationToken)
    {
        SqlProductionClientResult response;

        try
        {
            ReportRequest? previousReportRequest = null;
            if (reportRequest.PreviousRequestId is not null)
            {
                previousReportRequest = await _repository.GetByIdAsync(
                    reportRequest.PreviousRequestId,
                    cancellationToken);
            }

            var request = SqlProductionClientRequestFactory.Create(
                reportRequest,
                previousReportRequest,
                userDataScope,
                semanticPlan: semanticPlan);
            response = await _sqlProductionClient!.ProduceAsync(
                request,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (TimeoutException)
        {
            return new SqlPreparationOutcome(
                null,
                null,
                await FailAsync(
                    reportRequest,
                    TimeoutCode,
                    TimeoutMessage,
                    cancellationToken));
        }
        catch (Exception)
        {
            return new SqlPreparationOutcome(
                null,
                null,
                await FailAsync(
                    reportRequest,
                    SqlProductionFailedCode,
                    SqlProductionFailedMessage,
                    cancellationToken));
        }

        if (response is null)
        {
            return await InvalidSqlProductionResponseAsync(
                reportRequest,
                cancellationToken);
        }

        if (response.Decision != SqlProductionClientDecision.Accepted
            && response.ExecutionPlan is not null)
        {
            return await InvalidSqlProductionResponseAsync(
                reportRequest,
                cancellationToken);
        }

        if (response.CanonicalRequestJson is not null)
        {
            try
            {
                await _reportRequestService.RecordCanonicalRequestAsync(
                    new RecordCanonicalRequestCommand(
                        reportRequest.RequestId,
                        response.CanonicalRequestJson,
                        GetCurrentUtcTime(reportRequest)),
                    cancellationToken);
                reportRequest = await GetRequiredAsync(
                    reportRequest.RequestId,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                return new SqlPreparationOutcome(
                    null,
                    null,
                    await FailAsync(
                        reportRequest,
                        SqlProductionFailedCode,
                        SqlProductionFailedMessage,
                        cancellationToken));
            }
        }

        switch (response.Decision)
        {
            case SqlProductionClientDecision.NeedsClarification:
                {
                    var question = string.IsNullOrWhiteSpace(
                        response.UserMessage)
                        ? ClarificationFallback
                        : response.UserMessage.Trim();
                    await _reportRequestService.RequestClarificationAsync(
                        new RequestReportClarificationCommand(
                            reportRequest.RequestId,
                            question,
                            GetCurrentUtcTime(reportRequest)),
                        cancellationToken);
                    return new SqlPreparationOutcome(
                        null,
                        null,
                        CreateResult(await GetRequiredAsync(
                            reportRequest.RequestId,
                            cancellationToken)));
                }
            case SqlProductionClientDecision.Rejected:
                {
                    if (!IsSafeCode(response.RejectionCode))
                    {
                        return await InvalidSqlProductionResponseAsync(
                            reportRequest,
                            cancellationToken);
                    }

                    var rejectionMessage = string.IsNullOrWhiteSpace(
                        response.UserMessage)
                        ? RejectionFallback
                        : response.UserMessage.Trim();
                    await _reportRequestService.RejectAsync(
                        new RejectReportRequestCommand(
                            reportRequest.RequestId,
                            response.RejectionCode!,
                            rejectionMessage,
                            GetCurrentUtcTime(reportRequest)),
                        cancellationToken);
                    return new SqlPreparationOutcome(
                        null,
                        null,
                        CreateResult(await GetRequiredAsync(
                            reportRequest.RequestId,
                            cancellationToken)));
                }
            case SqlProductionClientDecision.Failed:
                return new SqlPreparationOutcome(
                    null,
                    null,
                    await FailAsync(
                        reportRequest,
                        SqlProductionFailedCode,
                        SqlProductionFailedMessage,
                        cancellationToken));
            case SqlProductionClientDecision.Accepted:
                break;
            default:
                return await InvalidSqlProductionResponseAsync(
                    reportRequest,
                    cancellationToken);
        }

        if (response.ExecutionPlan is null
            || string.IsNullOrWhiteSpace(
                response.ExecutionPlan.AppliedScopeFilter)
            || string.IsNullOrWhiteSpace(response.ExecutionPlan.Sql)
            || response.ExecutionPlan.CommandTimeoutSeconds <= 0
            || string.IsNullOrWhiteSpace(
                response.ExecutionPlan.VerifiedPhysicalObject)
            || response.ExecutionPlan.RowLimit is not > 0
            || _queryExecutionClient is null)
        {
            return await InvalidSqlProductionResponseAsync(
                reportRequest,
                cancellationToken);
        }

        var auditMetadata = ApplicationAuditMetadata.Create(
            reportRequest.ConversationId,
            response.ExecutionPlan,
            attemptNumber,
            deliveryId);
        await AppendQueryAuditAsync(
            reportRequest,
            ApplicationAuditEventType.QueryExecutionStarted,
            ApplicationAuditOutcome.Succeeded,
            response.ExecutionPlan.Source,
            TimeSpan.Zero,
            null,
            null,
            null,
            auditMetadata,
            cancellationToken);

        QueryExecutionResult executionResult;
        try
        {
            executionResult = await _queryExecutionClient.ExecuteAsync(
                response.ExecutionPlan,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (QueryExecutionException exception)
        {
            await AppendQueryAuditAsync(
                reportRequest,
                ApplicationAuditEventType.QueryExecutionFailed,
                ApplicationAuditOutcome.Failed,
                exception.DataSource,
                exception.Duration,
                null,
                null,
                exception.ErrorCode,
                auditMetadata,
                cancellationToken);
            return new SqlPreparationOutcome(
                null,
                null,
                await FailAsync(
                    reportRequest,
                    exception.ErrorCode,
                    exception.UserMessage,
                    cancellationToken));
        }
        catch (TimeoutException)
        {
            await AppendQueryAuditAsync(
                reportRequest,
                ApplicationAuditEventType.QueryExecutionFailed,
                ApplicationAuditOutcome.Failed,
                response.ExecutionPlan.Source,
                TimeSpan.Zero,
                null,
                null,
                QueryExecutionErrorCodes.Timeout,
                auditMetadata,
                cancellationToken);
            return new SqlPreparationOutcome(
                null,
                null,
                await FailAsync(
                    reportRequest,
                    QueryExecutionErrorCodes.Timeout,
                    QueryExecutionMessages.Timeout,
                    cancellationToken));
        }
        catch (Exception)
        {
            await AppendQueryAuditAsync(
                reportRequest,
                ApplicationAuditEventType.QueryExecutionFailed,
                ApplicationAuditOutcome.Failed,
                response.ExecutionPlan.Source,
                TimeSpan.Zero,
                null,
                null,
                QueryExecutionErrorCodes.Failed,
                auditMetadata,
                cancellationToken);
            return new SqlPreparationOutcome(
                null,
                null,
                await FailAsync(
                    reportRequest,
                    QueryExecutionFailedCode,
                    QueryExecutionFailedMessage,
                    cancellationToken));
        }

        if (executionResult is null
            || string.IsNullOrWhiteSpace(
                executionResult.ResultReference)
            || executionResult.Source != response.ExecutionPlan.Source)
        {
            return new SqlPreparationOutcome(
                null,
                null,
                await FailAsync(
                    reportRequest,
                    QueryExecutionFailedCode,
                    QueryExecutionFailedMessage,
                    cancellationToken));
        }

        await AppendQueryAuditAsync(
            reportRequest,
            ApplicationAuditEventType.QueryExecutionSucceeded,
            ApplicationAuditOutcome.Succeeded,
            executionResult.Source,
            executionResult.Duration,
            executionResult.RowCount,
            executionResult.IsTruncated,
            null,
            auditMetadata,
            cancellationToken);

        return new SqlPreparationOutcome(
            executionResult.ResultReference,
            executionResult,
            null,
            response.ExecutionPlan.ResultShape);
    }

    private async Task<SqlPreparationOutcome>
        InvalidSqlProductionResponseAsync(
            ReportRequest reportRequest,
            CancellationToken cancellationToken)
    {
        return new SqlPreparationOutcome(
            null,
            null,
            await FailAsync(
                reportRequest,
                InvalidSqlProductionResponseCode,
                InvalidSqlProductionResponseMessage,
                cancellationToken));
    }

    private async Task<ConversationContextSnapshot>
        LoadConversationContextAsync(
            ReportRequest reportRequest,
            CancellationToken cancellationToken)
    {
        ReportRequest? previousReportRequest = null;

        if (reportRequest.PreviousRequestId is not null)
        {
            previousReportRequest = await _repository.GetByIdAsync(
                reportRequest.PreviousRequestId,
                cancellationToken);

            if (previousReportRequest is not null
                && (!IdentifiersEqual(
                        previousReportRequest.UserId,
                        reportRequest.UserId)
                    || !IdentifiersEqual(
                        previousReportRequest.TenantId,
                        reportRequest.TenantId)))
            {
                previousReportRequest = null;
            }
        }

        return new ConversationContextSnapshot(
            ConversationId: reportRequest.ConversationId,
            PreviousRequestId: reportRequest.PreviousRequestId,
            PreviousSummary: previousReportRequest?.Summary,
            PreviousPowerBiUrl: previousReportRequest?.PowerBiUrl);
    }

    private static bool IdentifiersEqual(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left)
            || string.IsNullOrWhiteSpace(right))
        {
            return left is null && right is null;
        }

        return Guid.TryParse(left.Trim(), out var leftGuid)
            && Guid.TryParse(right.Trim(), out var rightGuid)
            ? leftGuid == rightGuid
            : string.Equals(
                left.Trim(),
                right.Trim(),
                StringComparison.Ordinal);
    }

    private static bool IsSafeCode(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && value.Length <= 128
            && value.All(character =>
                character is >= 'A' and <= 'Z'
                    or >= '0' and <= '9'
                    or '_');
    }

    private async Task<ProcessReportRequestResult> FailAsync(
        ReportRequest reportRequest,
        string errorCode,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        await _reportRequestService.FailAsync(
            new FailReportRequestCommand(
                RequestId: reportRequest.RequestId,
                ErrorCode: errorCode,
                ErrorMessage: errorMessage,
                FailedAt: GetCurrentUtcTime(reportRequest)),
            cancellationToken);

        return CreateResult(await GetRequiredAsync(
            reportRequest.RequestId,
            cancellationToken));
    }

    private Task AppendQueryAuditAsync(
        ReportRequest reportRequest,
        ApplicationAuditEventType eventType,
        ApplicationAuditOutcome outcome,
        SqlDataSource source,
        TimeSpan duration,
        int? rowCount,
        bool? resultTruncated,
        string? reasonCode,
        ApplicationAuditMetadata auditMetadata,
        CancellationToken cancellationToken)
    {
        if (_auditWriter is null)
        {
            return Task.CompletedTask;
        }

        var auditEvent = ApplicationAuditEventFactory.Create(
            eventType,
            outcome,
            GetCurrentUtcTime(reportRequest),
            reportRequest.RequestId,
            reportRequest.PreviousRequestId,
            reportRequest.CorrelationId,
            reportRequest.UserId,
            reportRequest.TenantId,
            reportRequest.Status.ToString(),
            reasonCode,
            source.ToString(),
            checked((long)duration.TotalMilliseconds),
            rowCount,
            resultTruncated,
            auditMetadata);
        return _auditWriter.AppendAsync(auditEvent, cancellationToken);
    }

    private async Task<ReportRequest> GetRequiredAsync(
        string requestId,
        CancellationToken cancellationToken)
    {
        return await _repository.GetByIdAsync(
                requestId,
                cancellationToken)
            ?? throw new KeyNotFoundException(
                $"A report request with ID '{requestId}' was not found.");
    }

    private static string GetSafeErrorCode(
        ExternalServiceError? error,
        string fallbackCode)
    {
        return !string.IsNullOrWhiteSpace(error?.Code)
            && error.Code.All(character =>
                character is >= 'A' and <= 'Z'
                    or >= '0' and <= '9'
                    or '_')
                ? error.Code
                : fallbackCode;
    }

    private static string GetSafeErrorMessage(
        ExternalServiceError? error,
        string fallbackMessage)
    {
        return string.IsNullOrWhiteSpace(error?.Message)
            ? fallbackMessage
            : error.Message.Trim();
    }

    private static DateTimeOffset GetCurrentUtcTime(
        ReportRequest reportRequest)
    {
        var utcNow = DateTimeOffset.UtcNow;
        return utcNow < reportRequest.UpdatedAt
            ? reportRequest.UpdatedAt
            : utcNow;
    }

    private static ProcessReportRequestResult CreateResult(
        ReportRequest reportRequest)
    {
        return new ProcessReportRequestResult(
            RequestId: reportRequest.RequestId,
            Status: reportRequest.Status,
            UpdatedAt: reportRequest.UpdatedAt,
            Summary: reportRequest.Summary,
            PowerBiUrl: reportRequest.PowerBiUrl,
            ClarificationQuestion:
                reportRequest.ClarificationQuestion,
            ErrorCode: reportRequest.ErrorCode,
            RejectionCode: reportRequest.RejectionCode,
            RejectionMessage: reportRequest.RejectionMessage);
    }

    private sealed record SqlPreparationOutcome(
        string? ResultReference,
        QueryExecutionResult? QueryResult,
        ProcessReportRequestResult? TerminalResult,
        SqlResultShapeMetadata? ResultShape = null);
}
