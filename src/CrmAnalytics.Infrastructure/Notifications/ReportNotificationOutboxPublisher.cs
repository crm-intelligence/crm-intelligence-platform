using CrmAnalytics.Application.Abstractions.Persistence;
using CrmAnalytics.Application.Notifications;
using CrmAnalytics.Application.Outbox;
using CrmAnalytics.Application.Teams;
using CrmAnalytics.Contracts.Notifications;
using CrmAnalytics.Contracts.InternalTeams;

namespace CrmAnalytics.Infrastructure.Notifications;

public interface IReportNotificationOutboxPublisher
{
    Task PublishAsync(ReportNotificationRequestedMessage message,
        CancellationToken cancellationToken);
}

public sealed class ReportNotificationOutboxPublisher(
    IReportRequestRepository reports,
    ITeamsNotificationTargetStore targets,
    ITeamsNotificationDeliveryStore deliveries,
    IInternalTeamsNotificationClient client,
    TimeProvider timeProvider) : IReportNotificationOutboxPublisher
{
    private readonly string _lockOwner =
        $"{Environment.MachineName}:{Guid.NewGuid():N}";

    public async Task PublishAsync(
        ReportNotificationRequestedMessage message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var report = await reports.GetByIdAsync(message.RequestId,
            cancellationToken) ?? throw new InvalidDataException(
                "Notification report does not exist.");
        var target = await targets.FindAsync(message.RequestId,
            cancellationToken);
        if (target is null)
            throw new TeamsNotificationException(
                System.Net.HttpStatusCode.Conflict);

        var deliveryId = TeamsDeliveryId.Create(message.RequestId,
            message.Status.ToString(), message.ReportUpdatedAt);
        var now = timeProvider.GetUtcNow();
        var claim = await deliveries.ClaimAsync(deliveryId,
            message.RequestId, message.Status.ToString(),
            message.ReportUpdatedAt, _lockOwner, now,
            TimeSpan.FromSeconds(60), cancellationToken);
        if (claim == TeamsDeliveryClaimResult.Delivered) return;
        if (claim == TeamsDeliveryClaimResult.Busy)
            throw new TeamsNotificationException(
                System.Net.HttpStatusCode.Conflict);

        try
        {
            await client.NotifyAsync(new InternalReportStatusNotificationRequest
            {
                DeliveryId = deliveryId,
                ConversationId = target.ConversationId,
                RequestId = report.RequestId,
                Status = message.Status,
                UpdatedAt = message.ReportUpdatedAt,
                CorrelationId = report.CorrelationId,
                Summary = report.Summary,
                PowerBiUrl = report.PowerBiUrl,
                VisualizationPreview = message.VisualizationPreview,
                ClarificationQuestion = report.ClarificationQuestion,
                ErrorCode = report.ErrorCode,
                RejectionMessage = report.RejectionMessage
            }, cancellationToken);
            await deliveries.MarkDeliveredAsync(deliveryId, _lockOwner,
                timeProvider.GetUtcNow(), cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            // The lock expires naturally. Cancellation does not consume an
            // outbox retry or force a dead-letter decision.
            throw;
        }
        catch
        {
            await deliveries.ReleaseAsync(deliveryId, _lockOwner,
                timeProvider.GetUtcNow(), CancellationToken.None);
            throw;
        }
    }
}
