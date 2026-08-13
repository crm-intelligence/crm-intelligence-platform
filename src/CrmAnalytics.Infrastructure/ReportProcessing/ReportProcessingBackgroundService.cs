using CrmAnalytics.Application.Notifications;
using CrmAnalytics.Application.ReportProcessing;
using CrmAnalytics.Application.ReportRequests;
using CrmAnalytics.Application.Outbox;
using CrmAnalytics.Domain.ReportRequests;
using CrmAnalytics.Contracts.Notifications;
using CrmAnalytics.Infrastructure.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrmAnalytics.Infrastructure.ReportProcessing;

public sealed class ReportProcessingBackgroundService : BackgroundService
{
    private const string UnexpectedFailureCode =
        "BACKGROUND_PROCESSING_FAILED";
    private const string UnexpectedFailureMessage =
        "Rapor işleme sırasında beklenmeyen bir hata oluştu.";

    private readonly IReportProcessingQueue _processingQueue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ReportProcessingQueueOptions _queueOptions;
    private readonly ILogger<ReportProcessingBackgroundService> _logger;

    public ReportProcessingBackgroundService(
        IReportProcessingQueue processingQueue,
        IServiceScopeFactory scopeFactory,
        IOptions<ReportProcessingQueueOptions> queueOptions,
        ILogger<ReportProcessingBackgroundService> logger)
    {
        ArgumentNullException.ThrowIfNull(processingQueue);
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(queueOptions);
        ArgumentNullException.ThrowIfNull(logger);

        _processingQueue = processingQueue;
        _scopeFactory = scopeFactory;
        _queueOptions = queueOptions.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        if (!_queueOptions.Enabled)
        {
            _logger.LogInformation(
                "Automatic report processing is disabled.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            QueuedReportProcessingRequest queuedRequest;

            try
            {
                queuedRequest = await _processingQueue.DequeueAsync(
                    stoppingToken);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            var shouldContinue = await ProcessItemAsync(
                queuedRequest,
                stoppingToken);

            if (!shouldContinue)
            {
                break;
            }
        }
    }

    private async Task<bool> ProcessItemAsync(
        QueuedReportProcessingRequest queuedRequest,
        CancellationToken stoppingToken)
    {
        using var loggingScope = _logger.BeginScope(
            new Dictionary<string, object?>
            {
                ["RequestId"] = queuedRequest.RequestId,
                ["CorrelationId"] = queuedRequest.CorrelationId
            });
        using var serviceScope = _scopeFactory.CreateScope();

        ProcessReportRequestResult result;

        try
        {
            var messageHandler = serviceScope.ServiceProvider
                .GetService<IReportProcessingMessageHandler>();
            if (messageHandler is not null)
            {
                result = await messageHandler.ProcessAsync(
                    new ReportProcessingRequestedMessage(
                        queuedRequest.RequestId,
                        queuedRequest.CorrelationId,
                        queuedRequest.EnqueuedAt,
                        queuedRequest.SemanticPlan is null
                            ? ReportProcessingRequestedMessage.LegacySchemaVersion
                            : ReportProcessingRequestedMessage.CurrentSchemaVersion,
                        queuedRequest.SemanticPlan),
                    stoppingToken);
            }
            else
            {
#pragma warning disable CS0618
                var scopeSnapshot = queuedRequest.UserDataScope
                    ?? throw new InvalidOperationException(
                        "A processing message handler is required.");
#pragma warning restore CS0618
                var processingService = serviceScope.ServiceProvider
                    .GetRequiredService<IReportProcessingService>();
                result = await processingService.ProcessAsync(
                    new ProcessReportRequestCommand(
                        queuedRequest.RequestId,
                        scopeSnapshot),
                    stoppingToken);
            }
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Unexpected background report processing failure.");

            if (stoppingToken.IsCancellationRequested)
            {
                return false;
            }

            var failedReport = await TryMarkAsFailedAsync(
                serviceScope.ServiceProvider,
                queuedRequest.RequestId,
                stoppingToken);
            if (failedReport is not null)
            {
                await TryNotifyCompatibilityAsync(
                    serviceScope.ServiceProvider,
                    CreateNotification(failedReport,
                        queuedRequest.CorrelationId),
                    stoppingToken);
            }

            return true;
        }

        _logger.LogInformation(
            "Report processing completed with status {Status}.",
            result.Status);

        var compatibilityNotification = CreateNotification(
            result, queuedRequest.CorrelationId);
        if (compatibilityNotification is not null)
        {
            await TryNotifyCompatibilityAsync(
                serviceScope.ServiceProvider,
                compatibilityNotification,
                stoppingToken);
        }

        return true;
    }

    private async Task<GetReportRequestResult?> TryMarkAsFailedAsync(
        IServiceProvider serviceProvider,
        string requestId,
        CancellationToken cancellationToken)
    {
        try
        {
            var reportRequestService = serviceProvider
                .GetRequiredService<IReportRequestService>();
            await reportRequestService.FailAsync(
                new FailReportRequestCommand(
                    RequestId: requestId,
                    ErrorCode: UnexpectedFailureCode,
                    ErrorMessage: UnexpectedFailureMessage,
                    FailedAt: DateTimeOffset.UtcNow),
                cancellationToken);

            return await reportRequestService.GetByIdAsync(
                requestId,
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception failureException)
        {
            _logger.LogError(
                failureException,
                "Could not mark report request as failed after "
                    + "background processing error.");
            return null;
        }
    }

    private async Task TryNotifyCompatibilityAsync(
        IServiceProvider serviceProvider,
        ReportStatusNotificationRequest notification,
        CancellationToken cancellationToken)
    {
        var client = serviceProvider
            .GetRequiredService<IReportStatusNotificationClient>();
        // The real runtime client is driven exclusively by the durable outbox.
        // This branch preserves custom Development/test clients used before 7.6.
        if (client is TeamsReportStatusNotificationClient) return;
        try
        {
            await client.NotifyAsync(notification, cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            _logger.LogError(
                "Compatibility report notification failed for request {RequestId} with status {Status}.",
                notification.RequestId, notification.Status);
        }
    }

    private static ReportStatusNotificationRequest? CreateNotification(
        ProcessReportRequestResult result, string correlationId)
    {
        var status = result.Status switch
        {
            ReportRequestStatus.Completed => ReportNotificationStatus.Completed,
            ReportRequestStatus.Failed => ReportNotificationStatus.Failed,
            ReportRequestStatus.WaitingForClarification =>
                ReportNotificationStatus.WaitingForClarification,
            ReportRequestStatus.Rejected => ReportNotificationStatus.Rejected,
            _ => (ReportNotificationStatus?)null
        };
        return status is null ? null : new ReportStatusNotificationRequest
        {
            RequestId = result.RequestId,
            Status = status.Value,
            UpdatedAt = result.UpdatedAt,
            CorrelationId = correlationId,
            Summary = result.Summary,
            PowerBiUrl = result.PowerBiUrl,
            ClarificationQuestion = result.ClarificationQuestion,
            ErrorCode = result.ErrorCode,
            RejectionMessage = result.RejectionMessage
        };
    }

    private static ReportStatusNotificationRequest CreateNotification(
        GetReportRequestResult result, string correlationId) => new()
    {
        RequestId = result.RequestId,
        Status = ReportNotificationStatus.Failed,
        UpdatedAt = result.UpdatedAt,
        CorrelationId = correlationId,
        Summary = result.Summary,
        PowerBiUrl = result.PowerBiUrl,
        ClarificationQuestion = result.ClarificationQuestion,
        ErrorCode = result.ErrorCode
    };

}
