using CrmAnalytics.Contracts.Integrations;
using CrmAnalytics.Application.Identity;

namespace CrmAnalytics.Application.ReportProcessing;

public sealed record QueuedReportProcessingRequest
{
    public QueuedReportProcessingRequest(
        string requestId,
        string correlationId,
        UserDataScope userDataScope,
        DateTimeOffset enqueuedAt)
        : this(requestId, correlationId, enqueuedAt, string.Empty)
    {
        ArgumentNullException.ThrowIfNull(userDataScope);
#pragma warning disable CS0618
        UserDataScope =
            UserDataScopeValidator.CreateSnapshot(userDataScope);
#pragma warning restore CS0618
    }

    public QueuedReportProcessingRequest(
        string requestId,
        string correlationId,
        DateTimeOffset enqueuedAt,
        string messageId,
        SubmittedSemanticPlanningResult? semanticPlan = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        if (enqueuedAt.Offset != TimeSpan.Zero)
            throw new ArgumentException(
                "The enqueue time must be in UTC.", nameof(enqueuedAt));
        RequestId = requestId.Trim();
        CorrelationId = correlationId.Trim();
        EnqueuedAt = enqueuedAt;
        MessageId = messageId?.Trim() ?? string.Empty;
        SemanticPlan = semanticPlan;
    }

    public string RequestId { get; }

    public string CorrelationId { get; }

    public UserDataScope? UserDataScope { get; }

    public DateTimeOffset EnqueuedAt { get; }

    public string MessageId { get; }

    public SubmittedSemanticPlanningResult? SemanticPlan { get; }
}
