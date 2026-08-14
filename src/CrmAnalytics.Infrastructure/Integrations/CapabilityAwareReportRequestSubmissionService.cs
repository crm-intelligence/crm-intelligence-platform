using CrmAnalytics.Application.Abstractions.Integrations;
using CrmAnalytics.Application.Abstractions.Persistence;
using CrmAnalytics.Application.Identity;
using CrmAnalytics.Application.ReportRequests;
using CrmAnalytics.Application.SqlAgent;
using CrmAnalytics.Application.SqlProduction;
using CrmAnalytics.Contracts.CopilotStudio;
using CrmAnalytics.Contracts.Integrations;
using CrmAnalytics.Contracts.SqlAgent;
using CrmAnalytics.Domain.ReportRequests;

namespace CrmAnalytics.Infrastructure.Integrations;

internal sealed class CapabilityAwareReportRequestSubmissionService(
    IReportRequestRepository repository,
    IReportRequestAccessService accessService,
    IReportRequestService reportRequestService,
    IPreparedSqlAgentCapabilityService sqlAgentService,
    ISqlProductionClient? sqlProductionClient = null)
    : IRoutedReportRequestSubmissionService
{
    private const string Deterministic = "deterministic";
    private const string AgenticRequired = "agentic_required";
    private const string Unsupported = "unsupported";
    private const string UnsupportedMessage =
        "The submitted analytical intent is not supported.";

    public async Task<RoutedReportRequestSubmissionResult>
        SubmitPlannedAsync(
            CreateReportRequestCommand command,
            AuthenticatedUserContext user,
            UserDataScope userDataScope,
            SubmittedSemanticPlanningResult semanticPlan,
            CopilotSqlAgentIntent intent,
            CancellationToken cancellationToken)
    {
        ValidateInputs(command, user, userDataScope, semanticPlan, intent);
        var productionClient = GetProductionClient();
        var requestId = Guid.NewGuid().ToString("N");
        var createdAt = DateTimeOffset.UtcNow;
        var preparedCommand = command with
        {
            PreparedRequestId = requestId,
            PreparedCreatedAt = createdAt
        };
        var preview = ReportRequest.Create(
            requestId,
            command.ConversationId,
            command.PreviousRequestId,
            command.Prompt,
            command.CorrelationId,
            user.UserId,
            user.TenantId,
            createdAt);
        var previous = await GetPreviousAsync(
            command.PreviousRequestId, cancellationToken);
        var planning = await productionClient.ProduceAsync(
            SqlProductionClientRequestFactory.Create(
                preview, previous, userDataScope,
                semanticPlan: semanticPlan),
            cancellationToken);
        if (planning.Decision == SqlProductionClientDecision.Failed)
        {
            return PlanningFailure(planning);
        }
        var capability = await AnalyzeAsync(
            requestId, command.ConversationId, planning, intent,
            cancellationToken);
        if (!capability.IsSuccessful
            || planning.Decision != SqlProductionClientDecision.Accepted
                && capability.Value?.Outcome != Unsupported)
        {
            return PlanOrCapabilityFailure(planning, capability);
        }

        var persisted = await PersistInitialAsync(
            preparedCommand, user, semanticPlan, planning,
            capability.Value!, cancellationToken);
        return Success(
            persisted.RequestId,
            persisted.Status,
            command.PreviousRequestId,
            command.ConversationId,
            capability.Value!);
    }

    public async Task<RoutedReportRequestSubmissionResult> RevisePlannedAsync(
        ReviseReportRequestCommand command,
        AuthenticatedUserContext user,
        UserDataScope userDataScope,
        SubmittedSemanticPlanningResult semanticPlan,
        CopilotSqlAgentIntent intent,
        CancellationToken cancellationToken)
    {
        ValidateInputs(command, user, userDataScope, semanticPlan, intent);
        var source = await GetOwnedAsync(
            command.SourceRequestId, user, cancellationToken);
        if (source.Status != ReportRequestStatus.Completed)
        {
            throw new InvalidOperationException(
                "Only completed report requests can be revised.");
        }
        var productionClient = GetProductionClient();

        var requestId = Guid.NewGuid().ToString("N");
        var createdAt = DateTimeOffset.UtcNow;
        var preparedCommand = command with
        {
            PreparedRequestId = requestId,
            PreparedCreatedAt = createdAt
        };
        var preview = ReportRequest.Create(
            requestId,
            source.ConversationId,
            source.RequestId,
            command.Prompt,
            command.CorrelationId,
            user.UserId,
            user.TenantId,
            createdAt);
        var planning = await productionClient.ProduceAsync(
            SqlProductionClientRequestFactory.Create(
                preview, source, userDataScope,
                semanticPlan: semanticPlan),
            cancellationToken);
        if (planning.Decision == SqlProductionClientDecision.Failed)
        {
            return PlanningFailure(planning);
        }
        var capability = await AnalyzeAsync(
            requestId, source.ConversationId, planning, intent,
            cancellationToken);
        if (!capability.IsSuccessful
            || planning.Decision != SqlProductionClientDecision.Accepted
                && capability.Value?.Outcome != Unsupported)
        {
            return PlanOrCapabilityFailure(planning, capability);
        }

        var persisted = await PersistRevisionAsync(
            preparedCommand, user, semanticPlan, planning,
            capability.Value!, cancellationToken);
        return Success(
            persisted.RequestId,
            persisted.Status,
            persisted.PreviousRequestId,
            persisted.ConversationId,
            capability.Value!);
    }

    public async Task<RoutedReportRequestSubmissionResult>
        SubmitPlannedClarificationAsync(
            SubmitReportClarificationCommand command,
            string correlationId,
            AuthenticatedUserContext user,
            UserDataScope userDataScope,
            SubmittedSemanticPlanningResult semanticPlan,
            CopilotSqlAgentIntent intent,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        ValidateInputs(user, userDataScope, semanticPlan, intent);
        var report = await GetOwnedAsync(
            command.RequestId, user, cancellationToken);
        EnsureCanAcceptClarification(report);
        var productionClient = GetProductionClient();

        var hasCanonical = !string.IsNullOrWhiteSpace(
            report.CanonicalRequestJson);
        var planningRequest = new SqlProductionClientRequest(
            report.RequestId,
            report.ConversationId,
            hasCanonical
                ? command.Response
                : string.Concat(
                    report.Prompt.Trim(), Environment.NewLine,
                    command.Response.Trim()),
            report.ReferenceDate,
            userDataScope,
            hasCanonical ? report.CanonicalRequestJson : null,
            report.UserId,
            OriginalPrompt: report.Prompt,
            ClarificationQuestion: report.ClarificationQuestion,
            ClarificationAnswer: command.Response,
            SemanticPlan: semanticPlan);
        var planning = await productionClient.ProduceAsync(
            planningRequest, cancellationToken);
        if (planning.Decision == SqlProductionClientDecision.Failed)
        {
            return PlanningFailure(planning);
        }
        var capability = await AnalyzeAsync(
            report.RequestId, report.ConversationId, planning, intent,
            cancellationToken);
        if (!capability.IsSuccessful
            || planning.Decision != SqlProductionClientDecision.Accepted
                && capability.Value?.Outcome != Unsupported)
        {
            return PlanOrCapabilityFailure(planning, capability);
        }

        var persisted = await PersistClarificationAsync(
            command, correlationId, user, semanticPlan, planning,
            capability.Value!, cancellationToken);
        return Success(
            persisted.RequestId,
            persisted.Status,
            report.PreviousRequestId,
            report.ConversationId,
            capability.Value!);
    }

    private async Task<CreateReportRequestResult> PersistInitialAsync(
        CreateReportRequestCommand command,
        AuthenticatedUserContext user,
        SubmittedSemanticPlanningResult semanticPlan,
        SqlProductionClientResult planning,
        SqlAgentCapabilityResponse capability,
        CancellationToken cancellationToken) => capability.Outcome switch
    {
        Deterministic => await reportRequestService.CreatePlannedAsync(
            command, user, semanticPlan, cancellationToken),
        AgenticRequired => await reportRequestService
            .CreatePlannedAwaitingAgenticAsync(
                command, user, semanticPlan,
                RequiredCanonical(planning), cancellationToken),
        Unsupported => await reportRequestService.CreateRejectedPlannedAsync(
            command, user, semanticPlan,
            RejectionCode(capability), UnsupportedMessage,
            cancellationToken),
        _ => throw new InvalidOperationException(
            "Unknown SQL-agent capability outcome.")
    };

    private async Task<ReviseReportRequestResult> PersistRevisionAsync(
        ReviseReportRequestCommand command,
        AuthenticatedUserContext user,
        SubmittedSemanticPlanningResult semanticPlan,
        SqlProductionClientResult planning,
        SqlAgentCapabilityResponse capability,
        CancellationToken cancellationToken) => capability.Outcome switch
    {
        Deterministic => await reportRequestService.RevisePlannedAsync(
            command, user, semanticPlan, cancellationToken),
        AgenticRequired => await reportRequestService
            .RevisePlannedAwaitingAgenticAsync(
                command, user, semanticPlan,
                RequiredCanonical(planning), cancellationToken),
        Unsupported => await reportRequestService.ReviseRejectedPlannedAsync(
            command, user, semanticPlan,
            RejectionCode(capability), UnsupportedMessage,
            cancellationToken),
        _ => throw new InvalidOperationException(
            "Unknown SQL-agent capability outcome.")
    };

    private async Task<UpdateReportRequestResult> PersistClarificationAsync(
        SubmitReportClarificationCommand command,
        string correlationId,
        AuthenticatedUserContext user,
        SubmittedSemanticPlanningResult semanticPlan,
        SqlProductionClientResult planning,
        SqlAgentCapabilityResponse capability,
        CancellationToken cancellationToken) => capability.Outcome switch
    {
        Deterministic => await reportRequestService
            .SubmitPlannedClarificationAsync(
                command, correlationId, user, semanticPlan,
                cancellationToken),
        AgenticRequired => await reportRequestService
            .SubmitPlannedClarificationAwaitingAgenticAsync(
                command, correlationId, user, semanticPlan,
                RequiredCanonical(planning), cancellationToken),
        Unsupported => await reportRequestService
            .RejectPlannedClarificationAsync(
                command, correlationId, user, semanticPlan,
                RejectionCode(capability), UnsupportedMessage,
                cancellationToken),
        _ => throw new InvalidOperationException(
            "Unknown SQL-agent capability outcome.")
    };

    private Task<SqlAgentServiceResult<SqlAgentCapabilityResponse>>
        AnalyzeAsync(
            string requestId,
            string conversationId,
            SqlProductionClientResult planning,
            CopilotSqlAgentIntent intent,
            CancellationToken cancellationToken) =>
        sqlAgentService.AnalyzePreparedCapabilityAsync(
            requestId,
            conversationId,
            planning.CanonicalRequestJson,
            intent,
            cancellationToken);

    private static RoutedReportRequestSubmissionResult
        PlanOrCapabilityFailure(
            SqlProductionClientResult planning,
            SqlAgentServiceResult<SqlAgentCapabilityResponse> capability)
    {
        if (planning.Decision == SqlProductionClientDecision.Accepted
            && !string.IsNullOrWhiteSpace(planning.CanonicalRequestJson))
        {
            return new RoutedReportRequestSubmissionResult(
                null, capability.Error, capability.StatusCode);
        }

        var statusCode = planning.Decision == SqlProductionClientDecision.Failed
            ? 500
            : 422;
        var status = planning.Decision == SqlProductionClientDecision.Failed
            ? "failed"
            : "rejected";
        var reason = planning.RejectionCode
            ?? (planning.Decision == SqlProductionClientDecision.NeedsClarification
                ? "PLAN_NEEDS_CLARIFICATION"
                : "CAPABILITY_ROUTING_FAILED");
        return new RoutedReportRequestSubmissionResult(
            null,
            new SqlAgentErrorResponse(status, reason),
            statusCode);
    }

    private static RoutedReportRequestSubmissionResult PlanningFailure(
        SqlProductionClientResult planning) =>
        PlanOrCapabilityFailure(
            planning,
            new SqlAgentServiceResult<SqlAgentCapabilityResponse>(
                null,
                new SqlAgentErrorResponse(
                    "failed", "CAPABILITY_ROUTING_FAILED"),
                500));

    private async Task<ReportRequest?> GetPreviousAsync(
        string? requestId,
        CancellationToken cancellationToken) =>
        string.IsNullOrWhiteSpace(requestId)
            ? null
            : await repository.GetByIdAsync(requestId, cancellationToken);

    private async Task<ReportRequest> GetOwnedAsync(
        string requestId,
        AuthenticatedUserContext user,
        CancellationToken cancellationToken)
    {
        var report = await repository.GetByIdAsync(
            requestId, cancellationToken)
            ?? throw new KeyNotFoundException(
                $"A report request with ID '{requestId}' was not found.");
        accessService.EnsureCanAccess(report, user);
        return report;
    }

    private static void EnsureCanAcceptClarification(ReportRequest report)
    {
        if (report.Status != ReportRequestStatus.WaitingForClarification
            || string.IsNullOrWhiteSpace(report.ClarificationQuestion)
            || report.ClarificationResponse is not null)
        {
            throw new InvalidOperationException(
                "The report request cannot accept a clarification response.");
        }
    }

    private static void ValidateInputs(
        object command,
        AuthenticatedUserContext user,
        UserDataScope userDataScope,
        SubmittedSemanticPlanningResult semanticPlan,
        CopilotSqlAgentIntent intent)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateInputs(user, userDataScope, semanticPlan, intent);
    }

    private static void ValidateInputs(
        AuthenticatedUserContext user,
        UserDataScope userDataScope,
        SubmittedSemanticPlanningResult semanticPlan,
        CopilotSqlAgentIntent intent)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(userDataScope);
        ArgumentNullException.ThrowIfNull(semanticPlan);
        ArgumentNullException.ThrowIfNull(intent);
        _ = UserDataScopeValidator.CreateRequiredSnapshot(
            userDataScope, user);
    }

    private static string RequiredCanonical(
        SqlProductionClientResult planning) =>
        !string.IsNullOrWhiteSpace(planning.CanonicalRequestJson)
            ? planning.CanonicalRequestJson
            : throw new InvalidOperationException(
                "Capability routing requires a validated canonical request.");

    private ISqlProductionClient GetProductionClient() =>
        sqlProductionClient
        ?? throw new InvalidOperationException(
            "Capability-aware routing requires the CrmAnalyticsSql provider.");

    private static string RejectionCode(
        SqlAgentCapabilityResponse capability)
    {
        var reason = capability.Reasons.FirstOrDefault();
        return string.IsNullOrWhiteSpace(reason)
            ? "UNSUPPORTED"
            : reason.ToUpperInvariant();
    }

    private static RoutedReportRequestSubmissionResult Success(
        string requestId,
        ReportRequestStatus status,
        string? previousRequestId,
        string conversationId,
        SqlAgentCapabilityResponse capability) => new(
        new CopilotRoutedReportResponse(
            requestId,
            status.ToString(),
            previousRequestId,
            conversationId,
            capability),
        null,
        202);
}
