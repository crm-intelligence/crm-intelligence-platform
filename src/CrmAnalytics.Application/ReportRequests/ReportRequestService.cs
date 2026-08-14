using CrmAnalytics.Application.Abstractions.Persistence;
using CrmAnalytics.Application.Auditing;
using CrmAnalytics.Application.Conversations;
using CrmAnalytics.Application.Identity;
using CrmAnalytics.Application.Outbox;
using CrmAnalytics.Domain.ReportRequests;
using CrmAnalytics.Contracts.Notifications;
using CrmAnalytics.Contracts.Integrations;

namespace CrmAnalytics.Application.ReportRequests;

public sealed class ReportRequestService : IReportRequestService
{
    private enum RoutedPersistenceAction
    {
        DispatchDeterministic,
        AwaitAgenticCandidate,
        RejectUnsupported
    }

    private readonly IReportRequestRepository _repository;
    private readonly IConversationContextService _conversationContextService;
    private readonly IReportRequestAccessService _accessService;
    private readonly IApplicationTransactionRunner? _transactionRunner;
    private readonly IApplicationAuditWriter? _auditWriter;
    private readonly IOutboxWriter? _outboxWriter;
    private readonly OutboxMessageFactory? _outboxMessageFactory;

    public ReportRequestService(
        IReportRequestRepository repository,
        IConversationContextService conversationContextService,
        IReportRequestAccessService? accessService = null)
        : this(repository, conversationContextService, null, null, null, null,
            accessService, true)
    {
    }

    public ReportRequestService(
        IReportRequestRepository repository,
        IConversationContextService conversationContextService,
        IApplicationTransactionRunner transactionRunner,
        IApplicationAuditWriter auditWriter,
        IReportRequestAccessService? accessService = null)
        : this(repository, conversationContextService,
            (IApplicationTransactionRunner?)transactionRunner,
            (IApplicationAuditWriter?)auditWriter,
            null, null, accessService,
            true)
    {
        ArgumentNullException.ThrowIfNull(transactionRunner);
        ArgumentNullException.ThrowIfNull(auditWriter);
    }

    public ReportRequestService(
        IReportRequestRepository repository,
        IConversationContextService conversationContextService,
        IApplicationTransactionRunner transactionRunner,
        IApplicationAuditWriter auditWriter,
        IOutboxWriter outboxWriter,
        OutboxMessageFactory outboxMessageFactory,
        IReportRequestAccessService? accessService = null)
        : this(repository, conversationContextService, transactionRunner,
            auditWriter, outboxWriter, outboxMessageFactory, accessService,
            true)
    {
        ArgumentNullException.ThrowIfNull(transactionRunner);
        ArgumentNullException.ThrowIfNull(auditWriter);
        ArgumentNullException.ThrowIfNull(outboxWriter);
        ArgumentNullException.ThrowIfNull(outboxMessageFactory);
    }

    private ReportRequestService(
        IReportRequestRepository repository,
        IConversationContextService conversationContextService,
        IApplicationTransactionRunner? transactionRunner,
        IApplicationAuditWriter? auditWriter,
        IOutboxWriter? outboxWriter,
        OutboxMessageFactory? outboxMessageFactory,
        IReportRequestAccessService? accessService,
        bool compatibilityConstructor)
    {
        _repository = repository;
        _conversationContextService = conversationContextService;
        _transactionRunner = transactionRunner;
        _auditWriter = auditWriter;
        _outboxWriter = outboxWriter;
        _outboxMessageFactory = outboxMessageFactory;
        _accessService = accessService ?? new ReportRequestAccessService();
        _ = compatibilityConstructor;
    }

    public Task<CreateReportRequestResult> CreateAsync(
        CreateReportRequestCommand command,
        CancellationToken cancellationToken) =>
        CreateCoreAsync(command, null, null,
            RoutedPersistenceAction.DispatchDeterministic,
            null, null, null, cancellationToken);

    public Task<CreateReportRequestResult> CreateAsync(
        CreateReportRequestCommand command,
        AuthenticatedUserContext user,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        return CreateCoreAsync(command, user, null,
            RoutedPersistenceAction.DispatchDeterministic,
            null, null, null, cancellationToken);
    }

    public Task<CreateReportRequestResult> CreatePlannedAsync(
        CreateReportRequestCommand command,
        AuthenticatedUserContext user,
        SubmittedSemanticPlanningResult semanticPlan,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(semanticPlan);
        return CreateCoreAsync(command, user, semanticPlan,
            RoutedPersistenceAction.DispatchDeterministic,
            null, null, null, cancellationToken);
    }

    public Task<CreateReportRequestResult> CreatePlannedAwaitingAgenticAsync(
        CreateReportRequestCommand command,
        AuthenticatedUserContext user,
        SubmittedSemanticPlanningResult semanticPlan,
        string canonicalRequestJson,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(semanticPlan);
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalRequestJson);
        return CreateCoreAsync(command, user, semanticPlan,
            RoutedPersistenceAction.AwaitAgenticCandidate,
            canonicalRequestJson, null, null, cancellationToken);
    }

    public Task<CreateReportRequestResult> CreateRejectedPlannedAsync(
        CreateReportRequestCommand command,
        AuthenticatedUserContext user,
        SubmittedSemanticPlanningResult semanticPlan,
        string rejectionCode,
        string rejectionMessage,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(semanticPlan);
        return CreateCoreAsync(command, user, semanticPlan,
            RoutedPersistenceAction.RejectUnsupported,
            null, rejectionCode, rejectionMessage, cancellationToken);
    }

    private async Task<CreateReportRequestResult> CreateCoreAsync(
        CreateReportRequestCommand command,
        AuthenticatedUserContext? user,
        SubmittedSemanticPlanningResult? semanticPlan,
        RoutedPersistenceAction persistenceAction,
        string? canonicalRequestJson,
        string? rejectionCode,
        string? rejectionMessage,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var reportRequest = ReportRequest.Create(
            command.PreparedRequestId ?? Guid.NewGuid().ToString("N"),
            command.ConversationId,
            command.PreviousRequestId,
            command.Prompt,
            command.CorrelationId,
            user?.UserId,
            user?.TenantId,
            command.PreparedCreatedAt,
            semanticPlanJson: semanticPlan is null
                ? null
                : SubmittedSemanticPlanSerializer.Serialize(semanticPlan));

        return await ExecuteAsync(async token =>
        {
            await _repository.AddAsync(reportRequest, token);
            if (user is null)
            {
                await _conversationContextService.RegisterRequestAsync(
                    command.ConversationId,
                    reportRequest.RequestId,
                    reportRequest.CreatedAt,
                    null,
                    token);
            }
            else
            {
                await _conversationContextService
                    .RegisterAuthenticatedRequestAsync(
                        command.ConversationId,
                        reportRequest.RequestId,
                        reportRequest.CreatedAt,
                        user,
                        token);
            }

            await AppendAuditAsync(reportRequest,
                ApplicationAuditEventType.ReportCreated,
                ApplicationAuditOutcome.Succeeded,
                reportRequest.CreatedAt,
                token);
            await ApplyInitialRoutingAsync(
                reportRequest, persistenceAction, canonicalRequestJson,
                rejectionCode, rejectionMessage, semanticPlan, token);
            return new CreateReportRequestResult(
                reportRequest.RequestId,
                reportRequest.Status);
        }, cancellationToken);
    }

    public Task<ReviseReportRequestResult> ReviseAsync(
        ReviseReportRequestCommand command,
        CancellationToken cancellationToken) =>
        ReviseCoreAsync(command, null, null,
            RoutedPersistenceAction.DispatchDeterministic,
            null, null, null, cancellationToken);

    public Task<ReviseReportRequestResult> ReviseAsync(
        ReviseReportRequestCommand command,
        AuthenticatedUserContext user,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        return ReviseCoreAsync(command, user, null,
            RoutedPersistenceAction.DispatchDeterministic,
            null, null, null, cancellationToken);
    }

    public Task<ReviseReportRequestResult> RevisePlannedAsync(
        ReviseReportRequestCommand command,
        AuthenticatedUserContext user,
        SubmittedSemanticPlanningResult semanticPlan,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(semanticPlan);
        return ReviseCoreAsync(
            command, user, semanticPlan,
            RoutedPersistenceAction.DispatchDeterministic,
            null, null, null, cancellationToken);
    }

    public Task<ReviseReportRequestResult> RevisePlannedAwaitingAgenticAsync(
        ReviseReportRequestCommand command,
        AuthenticatedUserContext user,
        SubmittedSemanticPlanningResult semanticPlan,
        string canonicalRequestJson,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(semanticPlan);
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalRequestJson);
        return ReviseCoreAsync(command, user, semanticPlan,
            RoutedPersistenceAction.AwaitAgenticCandidate,
            canonicalRequestJson, null, null, cancellationToken);
    }

    public Task<ReviseReportRequestResult> ReviseRejectedPlannedAsync(
        ReviseReportRequestCommand command,
        AuthenticatedUserContext user,
        SubmittedSemanticPlanningResult semanticPlan,
        string rejectionCode,
        string rejectionMessage,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(semanticPlan);
        return ReviseCoreAsync(command, user, semanticPlan,
            RoutedPersistenceAction.RejectUnsupported,
            null, rejectionCode, rejectionMessage, cancellationToken);
    }

    private async Task<ReviseReportRequestResult> ReviseCoreAsync(
        ReviseReportRequestCommand command,
        AuthenticatedUserContext? user,
        SubmittedSemanticPlanningResult? semanticPlan,
        RoutedPersistenceAction persistenceAction,
        string? canonicalRequestJson,
        string? rejectionCode,
        string? rejectionMessage,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var revisionRequestId = command.PreparedRequestId
            ?? Guid.NewGuid().ToString("N");
        var revisionCreatedAt = command.PreparedCreatedAt
            ?? DateTimeOffset.UtcNow;
        return await ExecuteAsync(async token =>
        {
            var source = await GetRequiredAsync(command.SourceRequestId, token);
            if (user is not null)
            {
                _accessService.EnsureCanAccess(source, user);
            }

            if (source.Status != ReportRequestStatus.Completed)
            {
                throw new InvalidOperationException(
                    "Only completed report requests can be revised.");
            }

            var report = ReportRequest.Create(
                revisionRequestId,
                source.ConversationId,
                source.RequestId,
                command.Prompt,
                command.CorrelationId,
                user?.UserId ?? source.UserId,
                user?.TenantId ?? source.TenantId,
                revisionCreatedAt,
                semanticPlan is null
                    ? null
                    : SubmittedSemanticPlanSerializer.Serialize(
                        semanticPlan));
            await _repository.AddAsync(report, token);
            if (user is null)
            {
                await _conversationContextService.RegisterRequestAsync(
                    report.ConversationId, report.RequestId,
                    report.CreatedAt, source.UserId, token);
            }
            else
            {
                await _conversationContextService
                    .RegisterAuthenticatedRequestAsync(
                        report.ConversationId, report.RequestId,
                        report.CreatedAt, user, token);
            }

            await AppendAuditAsync(report,
                ApplicationAuditEventType.ReportRevisionCreated,
                ApplicationAuditOutcome.Succeeded,
                report.CreatedAt,
                token,
                source.RequestId);
            await ApplyInitialRoutingAsync(
                report, persistenceAction, canonicalRequestJson,
                rejectionCode, rejectionMessage, semanticPlan, token);
            return new ReviseReportRequestResult(
                report.RequestId,
                source.RequestId,
                report.ConversationId,
                report.Status,
                report.CreatedAt);
        }, cancellationToken);
    }

    public Task<GetReportRequestResult?> GetByIdAsync(
        string requestId,
        CancellationToken cancellationToken) =>
        GetByIdCoreAsync(requestId, null, cancellationToken);

    public Task<GetReportRequestResult?> GetByIdAsync(
        string requestId,
        AuthenticatedUserContext user,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        return GetByIdCoreAsync(requestId, user, cancellationToken);
    }

    private async Task<GetReportRequestResult?> GetByIdCoreAsync(
        string requestId,
        AuthenticatedUserContext? user,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        var report = await _repository.GetByIdAsync(requestId, cancellationToken);
        if (report is null) return null;
        if (user is not null) _accessService.EnsureCanAccess(report, user);
        return new GetReportRequestResult(
            report.RequestId, report.ConversationId,
            report.PreviousRequestId, report.Status, report.CreatedAt,
            report.UpdatedAt, report.CorrelationId, report.ReportId,
            report.Summary, report.PowerBiUrl,
            report.ClarificationQuestion, report.ErrorCode,
            report.ErrorMessage, report.RejectionMessage);
    }

    public async Task<GetReportPlanningContextResult?> GetPlanningContextAsync(
        string requestId,
        AuthenticatedUserContext user,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        ArgumentNullException.ThrowIfNull(user);
        var report = await _repository.GetByIdAsync(
            requestId, cancellationToken);
        if (report is null) return null;
        _accessService.EnsureCanAccess(report, user);

        var semanticPlan = string.IsNullOrWhiteSpace(report.SemanticPlanJson)
            ? null
            : SubmittedSemanticPlanSerializer.Deserialize(
                report.SemanticPlanJson);
        return new GetReportPlanningContextResult(
            report.RequestId,
            report.ConversationId,
            report.Prompt,
            report.Status,
            report.ClarificationQuestion,
            semanticPlan,
            report.CanonicalRequestJson);
    }

    public async Task<UpdateReportRequestResult> TransitionStatusAsync(
        TransitionReportRequestStatusCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var report = await GetRequiredAsync(command.RequestId, cancellationToken);
        report.TransitionTo(command.TargetStatus, command.TransitionedAt);
        return await UpdateAndCreateResultAsync(report, cancellationToken);
    }

    public Task<UpdateReportRequestResult> CompleteAsync(
        CompleteReportRequestCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return ExecuteAsync(async token =>
        {
            var report = await GetRequiredAsync(command.RequestId, token);
            report.Complete(command.ReportId, command.Summary,
                command.PowerBiUrl, command.CompletedAt);
            var result = await UpdateAndCreateResultAsync(report, token);
            await AppendAuditAsync(report,
                ApplicationAuditEventType.ReportCompleted,
                ApplicationAuditOutcome.Succeeded,
                command.CompletedAt,
                token);
            await AppendNotificationOutboxAsync(
                report, token, command.VisualizationPreview);
            return result;
        }, cancellationToken);
    }

    public Task<UpdateReportRequestResult> FailAsync(
        FailReportRequestCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return MutateAndAuditAsync(command.RequestId, report =>
            report.Fail(command.ErrorCode, command.ErrorMessage,
                command.FailedAt),
            ApplicationAuditEventType.ReportFailed,
            ApplicationAuditOutcome.Failed,
            command.FailedAt, command.ErrorCode, cancellationToken);
    }

    public Task<UpdateReportRequestResult> RecordCanonicalRequestAsync(
        RecordCanonicalRequestCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return MutateAndAuditAsync(command.RequestId, report =>
            report.RecordCanonicalRequest(command.CanonicalRequestJson,
                command.RecordedAt),
            ApplicationAuditEventType.CanonicalRequestRecorded,
            ApplicationAuditOutcome.Succeeded,
            command.RecordedAt, null, cancellationToken);
    }

    public Task<UpdateReportRequestResult> RejectAsync(
        RejectReportRequestCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return MutateAndAuditAsync(command.RequestId, report =>
            report.Reject(command.RejectionCode, command.RejectionMessage,
                command.RejectedAt),
            ApplicationAuditEventType.ReportRejected,
            ApplicationAuditOutcome.Rejected,
            command.RejectedAt, command.RejectionCode, cancellationToken);
    }

    public Task<UpdateReportRequestResult> RequestClarificationAsync(
        RequestReportClarificationCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return MutateAndAuditAsync(command.RequestId, report =>
            report.RequestClarification(command.ClarificationQuestion,
                command.RequestedAt),
            ApplicationAuditEventType.ReportClarificationRequested,
            ApplicationAuditOutcome.Succeeded,
            command.RequestedAt, null, cancellationToken);
    }

    public Task<UpdateReportRequestResult> SubmitClarificationAsync(
        SubmitReportClarificationCommand command,
        CancellationToken cancellationToken) =>
        SubmitClarificationCoreAsync(
            command, null, null, null,
            RoutedPersistenceAction.DispatchDeterministic,
            null, null, null, cancellationToken);

    public Task<UpdateReportRequestResult> SubmitClarificationAsync(
        SubmitReportClarificationCommand command,
        AuthenticatedUserContext user,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        return SubmitClarificationCoreAsync(
            command, user, null, null,
            RoutedPersistenceAction.DispatchDeterministic,
            null, null, null, cancellationToken);
    }

    public Task<UpdateReportRequestResult> SubmitClarificationAsync(
        SubmitReportClarificationCommand command,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        return SubmitClarificationCoreAsync(
            command, null, correlationId.Trim(), null,
            RoutedPersistenceAction.DispatchDeterministic,
            null, null, null, cancellationToken);
    }

    public Task<UpdateReportRequestResult> SubmitClarificationAsync(
        SubmitReportClarificationCommand command,
        string correlationId,
        AuthenticatedUserContext user,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        return SubmitClarificationCoreAsync(command, user,
            correlationId.Trim(), null,
            RoutedPersistenceAction.DispatchDeterministic,
            null, null, null, cancellationToken);
    }

    public Task<UpdateReportRequestResult> SubmitPlannedClarificationAsync(
        SubmitReportClarificationCommand command,
        string correlationId,
        AuthenticatedUserContext user,
        SubmittedSemanticPlanningResult semanticPlan,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(semanticPlan);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        return SubmitClarificationCoreAsync(
            command, user, correlationId.Trim(), semanticPlan,
            RoutedPersistenceAction.DispatchDeterministic,
            null, null, null, cancellationToken);
    }

    public Task<UpdateReportRequestResult>
        SubmitPlannedClarificationAwaitingAgenticAsync(
            SubmitReportClarificationCommand command,
            string correlationId,
            AuthenticatedUserContext user,
            SubmittedSemanticPlanningResult semanticPlan,
            string canonicalRequestJson,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(semanticPlan);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalRequestJson);
        return SubmitClarificationCoreAsync(
            command, user, correlationId.Trim(), semanticPlan,
            RoutedPersistenceAction.AwaitAgenticCandidate,
            canonicalRequestJson, null, null, cancellationToken);
    }

    public Task<UpdateReportRequestResult> RejectPlannedClarificationAsync(
        SubmitReportClarificationCommand command,
        string correlationId,
        AuthenticatedUserContext user,
        SubmittedSemanticPlanningResult semanticPlan,
        string rejectionCode,
        string rejectionMessage,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(semanticPlan);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        return SubmitClarificationCoreAsync(
            command, user, correlationId.Trim(), semanticPlan,
            RoutedPersistenceAction.RejectUnsupported,
            null, rejectionCode, rejectionMessage, cancellationToken);
    }

    private async Task<UpdateReportRequestResult> SubmitClarificationCoreAsync(
        SubmitReportClarificationCommand command,
        AuthenticatedUserContext? user,
        string? correlationId,
        SubmittedSemanticPlanningResult? semanticPlan,
        RoutedPersistenceAction persistenceAction,
        string? canonicalRequestJson,
        string? rejectionCode,
        string? rejectionMessage,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return await ExecuteAsync(async token =>
        {
            var report = await GetRequiredAsync(command.RequestId, token);
            if (user is not null) _accessService.EnsureCanAccess(report, user);
            report.SubmitClarificationResponse(
                command.Response, command.SubmittedAt,
                semanticPlan is null
                    ? null
                    : SubmittedSemanticPlanSerializer.Serialize(
                        semanticPlan));
            var result = await UpdateAndCreateResultAsync(report, token);
            await AppendAuditAsync(report,
                ApplicationAuditEventType.ReportClarificationSubmitted,
                ApplicationAuditOutcome.Succeeded,
                command.SubmittedAt,
                token);
            switch (persistenceAction)
            {
                case RoutedPersistenceAction.DispatchDeterministic:
                    await AppendProcessingOutboxAsync(
                        report.RequestId,
                        correlationId ?? report.CorrelationId,
                        command.SubmittedAt,
                        "clarification",
                        semanticPlan,
                        token);
                    return result;
                case RoutedPersistenceAction.AwaitAgenticCandidate:
                    await PrepareForAgenticCandidateAsync(
                        report, canonicalRequestJson!, command.SubmittedAt,
                        token);
                    return CreateUpdateResult(report);
                case RoutedPersistenceAction.RejectUnsupported:
                    await RejectUnsupportedAsync(
                        report, rejectionCode!, rejectionMessage!,
                        command.SubmittedAt, token);
                    return CreateUpdateResult(report);
                default:
                    throw new InvalidOperationException(
                        "Unknown routed persistence action.");
            }
        }, cancellationToken);
    }

    private async Task ApplyInitialRoutingAsync(
        ReportRequest report,
        RoutedPersistenceAction persistenceAction,
        string? canonicalRequestJson,
        string? rejectionCode,
        string? rejectionMessage,
        SubmittedSemanticPlanningResult? semanticPlan,
        CancellationToken cancellationToken)
    {
        switch (persistenceAction)
        {
            case RoutedPersistenceAction.DispatchDeterministic:
                await AppendProcessingOutboxAsync(
                    report.RequestId,
                    report.CorrelationId,
                    report.CreatedAt,
                    "initial",
                    semanticPlan,
                    cancellationToken);
                break;
            case RoutedPersistenceAction.AwaitAgenticCandidate:
                await PrepareForAgenticCandidateAsync(
                    report, canonicalRequestJson!, report.CreatedAt,
                    cancellationToken);
                break;
            case RoutedPersistenceAction.RejectUnsupported:
                await RejectUnsupportedAsync(
                    report, rejectionCode!, rejectionMessage!,
                    report.CreatedAt, cancellationToken);
                break;
            default:
                throw new InvalidOperationException(
                    "Unknown routed persistence action.");
        }
    }

    private async Task PrepareForAgenticCandidateAsync(
        ReportRequest report,
        string canonicalRequestJson,
        DateTimeOffset routedAt,
        CancellationToken cancellationToken)
    {
        report.RecordCanonicalRequest(canonicalRequestJson, routedAt);
        report.TransitionTo(ReportRequestStatus.Validating, routedAt);
        report.TransitionTo(ReportRequestStatus.Processing, routedAt);
        await _repository.UpdateAsync(report, cancellationToken);
        await AppendAuditAsync(
            report,
            ApplicationAuditEventType.CanonicalRequestRecorded,
            ApplicationAuditOutcome.Succeeded,
            routedAt,
            cancellationToken);
    }

    private async Task RejectUnsupportedAsync(
        ReportRequest report,
        string rejectionCode,
        string rejectionMessage,
        DateTimeOffset routedAt,
        CancellationToken cancellationToken)
    {
        report.TransitionTo(ReportRequestStatus.Validating, routedAt);
        report.Reject(rejectionCode, rejectionMessage, routedAt);
        await _repository.UpdateAsync(report, cancellationToken);
        await AppendAuditAsync(
            report,
            ApplicationAuditEventType.ReportRejected,
            ApplicationAuditOutcome.Rejected,
            routedAt,
            cancellationToken,
            reasonCode: rejectionCode);
        await AppendNotificationOutboxAsync(report, cancellationToken);
    }

    private static UpdateReportRequestResult CreateUpdateResult(
        ReportRequest report) => new(
        report.RequestId,
        report.Status,
        report.UpdatedAt,
        report.RejectionCode,
        report.RejectionMessage);

    private async Task<UpdateReportRequestResult> MutateAndAuditAsync(
        string requestId,
        Action<ReportRequest> mutation,
        ApplicationAuditEventType eventType,
        ApplicationAuditOutcome outcome,
        DateTimeOffset occurredAt,
        string? reasonCode,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(async token =>
        {
            var report = await GetRequiredAsync(requestId, token);
            mutation(report);
            var result = await UpdateAndCreateResultAsync(report, token);
            await AppendAuditAsync(report, eventType, outcome,
                occurredAt, token, reasonCode: reasonCode);
            await AppendNotificationOutboxAsync(report, token);
            return result;
        }, cancellationToken);

    private async Task<ReportRequest> GetRequiredAsync(
        string requestId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        return await _repository.GetByIdAsync(requestId, cancellationToken)
            ?? throw new KeyNotFoundException(
                $"A report request with ID '{requestId}' was not found.");
    }

    private async Task<UpdateReportRequestResult> UpdateAndCreateResultAsync(
        ReportRequest report,
        CancellationToken cancellationToken)
    {
        await _repository.UpdateAsync(report, cancellationToken);
        return new UpdateReportRequestResult(
            report.RequestId, report.Status, report.UpdatedAt,
            report.RejectionCode, report.RejectionMessage);
    }

    private Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken) =>
        _transactionRunner is null
            ? operation(cancellationToken)
            : _transactionRunner.ExecuteAsync(operation, cancellationToken);

    private Task AppendAuditAsync(
        ReportRequest report,
        ApplicationAuditEventType eventType,
        ApplicationAuditOutcome outcome,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken,
        string? previousRequestId = null,
        string? reasonCode = null)
    {
        if (_auditWriter is null) return Task.CompletedTask;
        var auditEvent = ApplicationAuditEventFactory.Create(
            eventType, outcome, occurredAt, report.RequestId,
            previousRequestId ?? report.PreviousRequestId,
            report.CorrelationId, report.UserId, report.TenantId,
            report.Status.ToString(), reasonCode);
        return _auditWriter.AppendAsync(auditEvent, cancellationToken);
    }

    private Task AppendProcessingOutboxAsync(
        string requestId,
        string correlationId,
        DateTimeOffset requestedAt,
        string processingGeneration,
        SubmittedSemanticPlanningResult? semanticPlan,
        CancellationToken cancellationToken)
    {
        if (_outboxWriter is null || _outboxMessageFactory is null)
        {
            return Task.CompletedTask;
        }

        var message = _outboxMessageFactory
            .CreateReportProcessingRequested(
                requestId,
                correlationId,
                requestedAt,
                processingGeneration,
                semanticPlan);
        return _outboxWriter.AppendAsync(message, cancellationToken);
    }

    private Task AppendNotificationOutboxAsync(
        ReportRequest report,
        CancellationToken cancellationToken,
        ReportVisualizationPreview? visualizationPreview = null)
    {
        if (_outboxWriter is null || _outboxMessageFactory is null)
            return Task.CompletedTask;
        var status = report.Status switch
        {
            ReportRequestStatus.Completed => ReportNotificationStatus.Completed,
            ReportRequestStatus.Failed => ReportNotificationStatus.Failed,
            ReportRequestStatus.WaitingForClarification =>
                ReportNotificationStatus.WaitingForClarification,
            ReportRequestStatus.Rejected => ReportNotificationStatus.Rejected,
            _ => (ReportNotificationStatus?)null
        };
        if (status is null) return Task.CompletedTask;
        return _outboxWriter.AppendAsync(
            _outboxMessageFactory.CreateReportNotificationRequested(
                report.RequestId, status.Value, report.UpdatedAt,
                status == ReportNotificationStatus.Completed
                    ? visualizationPreview
                    : null),
            cancellationToken);
    }
}
