using CrmAnalytics.Application.Abstractions.Persistence;
using CrmAnalytics.Application.Exceptions;
using CrmAnalytics.Application.Identity;
using CrmAnalytics.Application.Outbox;
using CrmAnalytics.Application.ReportProcessing;
using CrmAnalytics.Application.ReportRequests;
using CrmAnalytics.Contracts.Integrations;
using CrmAnalytics.Domain.ReportRequests;

namespace CrmAnalytics.Infrastructure.ReportProcessing;

public interface IReportProcessingMessageHandler
{
    Task<ProcessReportRequestResult> ProcessAsync(
        ReportProcessingRequestedMessage message,
        CancellationToken cancellationToken,
        ReportProcessingDeliveryContext? deliveryContext = null);
}

public sealed record ReportProcessingDeliveryContext
{
    public ReportProcessingDeliveryContext(
        int attemptNumber,
        string? deliveryId)
    {
        if (attemptNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(attemptNumber));
        }

        AttemptNumber = attemptNumber;
        DeliveryId = string.IsNullOrWhiteSpace(deliveryId)
            ? null
            : deliveryId.Trim();
    }

    public int AttemptNumber { get; }
    public string? DeliveryId { get; }
}

public sealed class ReportProcessingPermanentException : Exception
{
    public ReportProcessingPermanentException(string reason)
        : base("Report processing cannot be completed.") => Reason = reason;
    public string Reason { get; }
}

public sealed class ReportProcessingTransientException : Exception
{
    public ReportProcessingTransientException(Exception? innerException = null)
        : base("Report processing should be retried.", innerException) { }
}

public sealed class ReportProcessingMessageHandler
    : IReportProcessingMessageHandler
{
    private const string ScopeUnavailableCode = "DATA_SCOPE_UNAVAILABLE";
    private const string ScopeUnavailableMessage =
        "Rapor talebi için güncel veri erişim yetkisi bulunamadı.";
    private const string PolicyErrorCode = "DATA_ACCESS_POLICY_ERROR";
    private const string PolicyErrorMessage =
        "Veri erişim politikası doğrulanamadı.";

    private readonly IReportRequestRepository _repository;
    private readonly IUserDataAccessAssignmentStore _assignmentStore;
    private readonly IReportProcessingService _processingService;
    private readonly IReportRequestService _requestService;
    private readonly TimeProvider _timeProvider;

    public ReportProcessingMessageHandler(
        IReportRequestRepository repository,
        IUserDataAccessAssignmentStore assignmentStore,
        IReportProcessingService processingService,
        IReportRequestService requestService,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _assignmentStore = assignmentStore;
        _processingService = processingService;
        _requestService = requestService;
        _timeProvider = timeProvider;
    }

    public async Task<ProcessReportRequestResult> ProcessAsync(
        ReportProcessingRequestedMessage message,
        CancellationToken cancellationToken,
        ReportProcessingDeliveryContext? deliveryContext = null)
    {
        ArgumentNullException.ThrowIfNull(message);
        var report = await _repository.GetByIdAsync(
            message.RequestId, cancellationToken);
        if (report is null)
            throw new ReportProcessingPermanentException(
                "ReportRequestNotFound");
        if (IsTerminal(report.Status)) return ToResult(report);

        if (report.Status != ReportRequestStatus.Received
            && !(report.Status == ReportRequestStatus.WaitingForClarification
                && !string.IsNullOrWhiteSpace(report.ClarificationResponse)))
            throw new ReportProcessingTransientException();

        if (string.IsNullOrWhiteSpace(report.UserId)
            || string.IsNullOrWhiteSpace(report.TenantId))
            return await FailPolicyAsync(report, PolicyErrorCode,
                PolicyErrorMessage, cancellationToken);

        UserDataAccessAssignment? assignment;
        try
        {
            assignment = await _assignmentStore.FindAsync(
                report.TenantId, report.UserId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (DataAccessPolicyIntegrityException)
        {
            return await FailPolicyAsync(report, PolicyErrorCode,
                PolicyErrorMessage, cancellationToken);
        }

        if (assignment is null)
            return await RejectUnavailableAsync(report, cancellationToken);

        var scope = UserDataScopeValidator.CreateRequiredSnapshot(
            new UserDataScope
            {
                UserId = report.UserId,
                TenantId = report.TenantId,
                Roles = [],
                AllowAllRegions = assignment.AllowAllRegions,
                AllowAllStores = assignment.AllowAllStores,
                AllowedRegions = assignment.AllowedRegions.ToArray(),
                AllowedStoreIds = assignment.AllowedStoreIds.ToArray()
            });

        try
        {
            return await _processingService.ProcessAsync(
                new ProcessReportRequestCommand(
                    report.RequestId,
                    scope,
                    deliveryContext?.AttemptNumber ?? 1,
                    deliveryContext?.DeliveryId,
                    message.SemanticPlan),
                cancellationToken);
        }
        catch (PersistenceConcurrencyException exception)
        {
            throw new ReportProcessingTransientException(exception);
        }
    }

    private async Task<ProcessReportRequestResult> RejectUnavailableAsync(
        ReportRequest report, CancellationToken cancellationToken)
    {
        var now = CurrentTime(report);
        if (report.Status is ReportRequestStatus.Received
            or ReportRequestStatus.WaitingForClarification)
        {
            await _requestService.TransitionStatusAsync(
                new TransitionReportRequestStatusCommand(
                    report.RequestId, ReportRequestStatus.Validating, now),
                cancellationToken);
            now = now.AddTicks(1);
        }
        await _requestService.RejectAsync(new RejectReportRequestCommand(
            report.RequestId, ScopeUnavailableCode, ScopeUnavailableMessage,
            now), cancellationToken);
        return ToResult((await _repository.GetByIdAsync(
            report.RequestId, cancellationToken))!);
    }

    private async Task<ProcessReportRequestResult> FailPolicyAsync(
        ReportRequest report, string code, string message,
        CancellationToken cancellationToken)
    {
        await _requestService.FailAsync(new FailReportRequestCommand(
            report.RequestId, code, message, CurrentTime(report)),
            cancellationToken);
        return ToResult((await _repository.GetByIdAsync(
            report.RequestId, cancellationToken))!);
    }

    private DateTimeOffset CurrentTime(ReportRequest report)
    {
        var now = _timeProvider.GetUtcNow();
        return now < report.UpdatedAt ? report.UpdatedAt : now;
    }

    private static bool IsTerminal(ReportRequestStatus status) =>
        status is ReportRequestStatus.Completed
            or ReportRequestStatus.Failed
            or ReportRequestStatus.Rejected;

    private static ProcessReportRequestResult ToResult(ReportRequest report) =>
        new(report.RequestId, report.Status, report.UpdatedAt,
            report.Summary, report.PowerBiUrl, report.ClarificationQuestion,
            report.ErrorCode, report.RejectionCode, report.RejectionMessage);
}
