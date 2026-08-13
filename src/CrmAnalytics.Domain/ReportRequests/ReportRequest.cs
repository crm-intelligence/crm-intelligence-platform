namespace CrmAnalytics.Domain.ReportRequests;

public sealed class ReportRequest {
    private static readonly IReadOnlyDictionary<
        ReportRequestStatus,
        IReadOnlySet<ReportRequestStatus>> AllowedTransitions =
        new Dictionary<
            ReportRequestStatus,
            IReadOnlySet<ReportRequestStatus>> {
            [ReportRequestStatus.Received] =
                new HashSet<ReportRequestStatus> {
                    ReportRequestStatus.Validating,
                    ReportRequestStatus.Failed
                },
            [ReportRequestStatus.Validating] =
                new HashSet<ReportRequestStatus> {
                    ReportRequestStatus.Queued,
                    ReportRequestStatus.Processing,
                    ReportRequestStatus.WaitingForClarification,
                    ReportRequestStatus.Failed,
                    ReportRequestStatus.Rejected
                },
            [ReportRequestStatus.Queued] =
                new HashSet<ReportRequestStatus> {
                    ReportRequestStatus.Running,
                    ReportRequestStatus.Failed
                },
            [ReportRequestStatus.Processing] =
                new HashSet<ReportRequestStatus> {
                    ReportRequestStatus.WaitingForClarification,
                    ReportRequestStatus.Completed,
                    ReportRequestStatus.Failed,
                    ReportRequestStatus.Rejected
                },
            [ReportRequestStatus.Running] =
                new HashSet<ReportRequestStatus> {
                    ReportRequestStatus.WaitingForClarification,
                    ReportRequestStatus.Completed,
                    ReportRequestStatus.Failed,
                    ReportRequestStatus.Rejected
                },
            [ReportRequestStatus.WaitingForClarification] =
                new HashSet<ReportRequestStatus> {
                    ReportRequestStatus.Validating,
                    ReportRequestStatus.Processing,
                    ReportRequestStatus.Failed
                },
            [ReportRequestStatus.Completed] =
                new HashSet<ReportRequestStatus>(),
            [ReportRequestStatus.Failed] =
                new HashSet<ReportRequestStatus>(),
            [ReportRequestStatus.Rejected] =
                new HashSet<ReportRequestStatus>()
        };

    private ReportRequest(
        string requestId,
        string conversationId,
        string? previousRequestId,
        string prompt,
        string correlationId,
        string? userId,
        string? tenantId,
        DateTimeOffset createdAt) {
        RequestId = requestId;
        UserId = userId;
        TenantId = tenantId;
        ConversationId = conversationId;
        PreviousRequestId = previousRequestId;
        Prompt = prompt;
        CorrelationId = correlationId;
        Status = ReportRequestStatus.Received;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
        ReferenceDate = DateOnly.FromDateTime(createdAt.UtcDateTime);
    }

    public string RequestId { get; private set; }

    public string? UserId { get; private set; }

    public string? TenantId { get; private set; }

    public string ConversationId { get; private set; }

    public string? PreviousRequestId { get; private set; }

    public string Prompt { get; private set; }

    public ReportRequestStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateOnly ReferenceDate { get; private set; }

    public string CorrelationId { get; private set; }

    public string? ReportId { get; private set; }

    public string? PowerBiUrl { get; private set; }

    public string? Summary { get; private set; }

    public string? ErrorCode { get; private set; }

    public string? ErrorMessage { get; private set; }

    public string? ClarificationQuestion { get; private set; }

    public string? ClarificationResponse { get; private set; }

    public string? CanonicalRequestJson { get; private set; }

    public string? SemanticPlanJson { get; private set; }

    public string? RejectionCode { get; private set; }

    public string? RejectionMessage { get; private set; }

    public static ReportRequest Create(
        string requestId,
        string conversationId,
        string? previousRequestId,
        string prompt,
        string correlationId,
        string? userId = null,
        string? tenantId = null,
        DateTimeOffset? createdAt = null,
        string? semanticPlanJson = null) {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        var effectiveCreatedAt = createdAt ?? DateTimeOffset.UtcNow;
        if (effectiveCreatedAt.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "The creation time must be in UTC.",
                nameof(createdAt));
        }

        var report = new ReportRequest(
            requestId: requestId,
            conversationId: conversationId.Trim(),
            previousRequestId: string.IsNullOrWhiteSpace(previousRequestId)
                ? null
                : previousRequestId.Trim(),
            prompt: prompt.Trim(),
            correlationId: correlationId,
            userId: string.IsNullOrWhiteSpace(userId)
                ? null
                : userId.Trim(),
            tenantId: string.IsNullOrWhiteSpace(tenantId)
                ? null
                : tenantId.Trim(),
            createdAt: effectiveCreatedAt);
        if (semanticPlanJson is not null)
        {
            report.SetSemanticPlan(semanticPlanJson);
        }

        return report;
    }

    public void SetSemanticPlan(string semanticPlanJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(semanticPlanJson);
        if (semanticPlanJson.Length > 65_536)
        {
            throw new ArgumentException(
                "The semantic plan JSON cannot exceed 65536 characters.",
                nameof(semanticPlanJson));
        }

        SemanticPlanJson = semanticPlanJson;
    }

    public void TransitionTo(
        ReportRequestStatus newStatus,
        DateTimeOffset transitionedAt) {
        if (!Enum.IsDefined(newStatus)) {
            throw new ArgumentOutOfRangeException(
                nameof(newStatus),
                newStatus,
                "The report request status is not defined.");
        }

        if (transitionedAt.Offset != TimeSpan.Zero) {
            throw new ArgumentException(
                "The transition time must be in UTC.",
                nameof(transitionedAt));
        }

        if (transitionedAt < UpdatedAt) {
            throw new ArgumentOutOfRangeException(
                nameof(transitionedAt),
                transitionedAt,
                "The transition time cannot be earlier than UpdatedAt.");
        }

        if (!AllowedTransitions[Status].Contains(newStatus)) {
            throw new InvalidOperationException(
                $"The report request cannot transition from "
                    + $"'{Status}' to '{newStatus}'.");
        }

        var clearsClarification =
            Status == ReportRequestStatus.WaitingForClarification
            && newStatus is ReportRequestStatus.Validating
                or ReportRequestStatus.Processing;

        Status = newStatus;
        UpdatedAt = transitionedAt;

        if (clearsClarification)
        {
            ClarificationQuestion = null;
        }
    }

    public void RequestClarification(
        string clarificationQuestion,
        DateTimeOffset requestedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clarificationQuestion);

        var normalizedClarificationQuestion = clarificationQuestion.Trim();

        if (normalizedClarificationQuestion.Length > 1000)
        {
            throw new ArgumentException(
                "The clarification question cannot exceed 1000 characters.",
                nameof(clarificationQuestion));
        }

        TransitionTo(
            ReportRequestStatus.WaitingForClarification,
            requestedAt);

        ClarificationQuestion = normalizedClarificationQuestion;
        ClarificationResponse = null;
        ErrorCode = null;
        ErrorMessage = null;
        RejectionCode = null;
        RejectionMessage = null;
    }

    public void SubmitClarificationResponse(
        string clarificationResponse,
        DateTimeOffset submittedAt,
        string? semanticPlanJson = null)
    {
        if (Status != ReportRequestStatus.WaitingForClarification)
        {
            throw new InvalidOperationException(
                "Only report requests waiting for clarification can "
                + "accept a response.");
        }

        if (string.IsNullOrWhiteSpace(ClarificationQuestion))
        {
            throw new InvalidOperationException(
                "A clarification response requires an active question.");
        }

        if (ClarificationResponse is not null)
        {
            throw new InvalidOperationException(
                "A clarification response has already been submitted.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(
            clarificationResponse);
        var normalizedResponse = clarificationResponse.Trim();

        if (normalizedResponse.Length is < 3 or > 2000)
        {
            throw new ArgumentException(
                "The clarification response must be between 3 and "
                + "2000 characters.",
                nameof(clarificationResponse));
        }

        if (submittedAt.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "The submission time must be in UTC.",
                nameof(submittedAt));
        }

        if (submittedAt < UpdatedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(submittedAt),
                submittedAt,
                "The submission time cannot be earlier than UpdatedAt.");
        }

        ClarificationResponse = normalizedResponse;
        if (semanticPlanJson is not null)
        {
            SetSemanticPlan(semanticPlanJson);
        }
        UpdatedAt = submittedAt;
    }

    public void Complete(
        string reportId,
        string summary,
        string powerBiUrl,
        DateTimeOffset completedAt) {
        ArgumentException.ThrowIfNullOrWhiteSpace(reportId);
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        ArgumentException.ThrowIfNullOrWhiteSpace(powerBiUrl);

        var normalizedPowerBiUrl = powerBiUrl.Trim();

        if (!Uri.TryCreate(
                normalizedPowerBiUrl,
                UriKind.Absolute,
                out var parsedPowerBiUrl)
            || !string.Equals(
                parsedPowerBiUrl.Scheme,
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(parsedPowerBiUrl.Host)) {
            throw new ArgumentException(
                "The Power BI URL must be a valid absolute HTTPS URL.",
                nameof(powerBiUrl));
        }

        TransitionTo(ReportRequestStatus.Completed, completedAt);

        ReportId = reportId.Trim();
        Summary = summary.Trim();
        PowerBiUrl = normalizedPowerBiUrl;
        ErrorCode = null;
        ErrorMessage = null;
        RejectionCode = null;
        RejectionMessage = null;
        ClarificationQuestion = null;
        ClarificationResponse = null;
    }

    public void Fail(
        string errorCode,
        string errorMessage,
        DateTimeOffset failedAt) {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);

        var normalizedErrorCode = errorCode.Trim();

        if (normalizedErrorCode.Any(
                character =>
                    character is not (>= 'A' and <= 'Z')
                    and not (>= '0' and <= '9')
                    and not '_')) {
            throw new ArgumentException(
                "The error code may contain only uppercase letters, "
                    + "digits, and underscores.",
                nameof(errorCode));
        }

        TransitionTo(ReportRequestStatus.Failed, failedAt);

        ErrorCode = normalizedErrorCode;
        ErrorMessage = errorMessage.Trim();
        ReportId = null;
        Summary = null;
        PowerBiUrl = null;
        ClarificationQuestion = null;
        ClarificationResponse = null;
        RejectionCode = null;
        RejectionMessage = null;
    }

    public void Reject(
        string rejectionCode,
        string rejectionMessage,
        DateTimeOffset rejectedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rejectionCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(rejectionMessage);

        var normalizedRejectionCode = rejectionCode.Trim();
        var normalizedRejectionMessage = rejectionMessage.Trim();

        if (normalizedRejectionCode.Length > 128)
        {
            throw new ArgumentException(
                "The rejection code cannot exceed 128 characters.",
                nameof(rejectionCode));
        }

        if (normalizedRejectionCode.Any(
                character =>
                    character is not (>= 'A' and <= 'Z')
                    and not (>= '0' and <= '9')
                    and not '_'))
        {
            throw new ArgumentException(
                "The rejection code may contain only uppercase letters, "
                    + "digits, and underscores.",
                nameof(rejectionCode));
        }

        if (normalizedRejectionMessage.Length > 1000)
        {
            throw new ArgumentException(
                "The rejection message cannot exceed 1000 characters.",
                nameof(rejectionMessage));
        }

        TransitionTo(ReportRequestStatus.Rejected, rejectedAt);

        RejectionCode = normalizedRejectionCode;
        RejectionMessage = normalizedRejectionMessage;
        ErrorCode = null;
        ErrorMessage = null;
        ReportId = null;
        Summary = null;
        PowerBiUrl = null;
        ClarificationQuestion = null;
        ClarificationResponse = null;
    }

    public void RecordCanonicalRequest(
        string canonicalRequestJson,
        DateTimeOffset recordedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalRequestJson);

        if (recordedAt.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "The canonical request recording time must be in UTC.",
                nameof(recordedAt));
        }

        if (recordedAt < UpdatedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(recordedAt),
                recordedAt,
                "The canonical request recording time cannot be earlier "
                    + "than UpdatedAt.");
        }

        if (Status is ReportRequestStatus.Completed
            or ReportRequestStatus.Failed
            or ReportRequestStatus.Rejected)
        {
            throw new InvalidOperationException(
                "A canonical request cannot be recorded for a terminal "
                    + "report request.");
        }

        CanonicalRequestJson = canonicalRequestJson;
        UpdatedAt = recordedAt;
    }
}
