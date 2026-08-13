using CrmAnalytics.Application.Outbox;
using CrmAnalytics.Infrastructure.Messaging;
using CrmAnalytics.Infrastructure.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrmAnalytics.Infrastructure.Outbox;

public sealed class OutboxDispatcherBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IReportProcessingMessagePublisher _publisher;
    private readonly IOutboxMessageSerializer _serializer;
    private readonly IMessagePublishFailureClassifier _classifier;
    private readonly OutboxDispatcherOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OutboxDispatcherBackgroundService> _logger;
    private readonly MessagingRuntimeState _runtimeState;
    private readonly string _lockOwner =
        $"{Environment.MachineName}:{Guid.NewGuid():N}";

    public OutboxDispatcherBackgroundService(
        IServiceScopeFactory scopeFactory,
        IReportProcessingMessagePublisher publisher,
        IOutboxMessageSerializer serializer,
        IMessagePublishFailureClassifier classifier,
        IOptions<OutboxDispatcherOptions> options,
        TimeProvider timeProvider,
        MessagingRuntimeState runtimeState,
        ILogger<OutboxDispatcherBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _publisher = publisher;
        _serializer = serializer;
        _classifier = classifier;
        _options = options.Value;
        _timeProvider = timeProvider;
        _runtimeState = runtimeState;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled) return;
        _runtimeState.DispatcherRunning = true;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DispatchOnceAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(
                    _options.PollingIntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception)
            {
                _logger.LogError(
                    "The outbox dispatch polling cycle failed.");
                _runtimeState.BrokerFailureObserved = true;
                await Task.Delay(TimeSpan.FromSeconds(
                    _options.PollingIntervalSeconds), stoppingToken);
            }
        }
        _runtimeState.DispatcherRunning = false;
    }

    public async Task<int> DispatchOnceAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<ClaimedOutboxMessage> claimed;
        var now = _timeProvider.GetUtcNow();
        using (var scope = _scopeFactory.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
            claimed = await store.ClaimPendingBatchAsync(
                _options.BatchSize,
                _lockOwner,
                now,
                TimeSpan.FromSeconds(_options.LockDurationSeconds),
                cancellationToken);
        }

        foreach (var item in claimed)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await DispatchItemAsync(item, cancellationToken);
        }
        return claimed.Count;
    }

    private async Task DispatchItemAsync(
        ClaimedOutboxMessage item,
        CancellationToken cancellationToken)
    {
        try
        {
            switch (item.MessageType)
            {
                case OutboxMessageType.ReportProcessingRequested:
                {
                    var envelope = _serializer
                        .DeserializeReportProcessingRequested(item.PayloadJson);
                    if (!string.Equals(envelope.RequestId, item.AggregateId,
                            StringComparison.Ordinal))
                        throw new InvalidDataException("Outbox aggregate mismatch.");
                    await _publisher.PublishAsync(
                        envelope, item.MessageId, cancellationToken);
                    break;
                }
                case OutboxMessageType.ReportNotificationRequested:
                {
                    var envelope = _serializer
                        .DeserializeReportNotificationRequested(item.PayloadJson);
                    if (!string.Equals(envelope.RequestId, item.AggregateId,
                            StringComparison.Ordinal))
                        throw new InvalidDataException("Outbox aggregate mismatch.");
                    using var scope = _scopeFactory.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<
                        IReportNotificationOutboxPublisher>()
                        .PublishAsync(envelope, cancellationToken);
                    break;
                }
                default:
                    throw new NotSupportedException("Unknown outbox message type.");
            }
            _runtimeState.BrokerFailureObserved = false;
            await WithStoreAsync(store => store.MarkPublishedAsync(
                item.MessageId, item.LockOwner,
                _timeProvider.GetUtcNow(), cancellationToken));
            _logger.LogInformation(
                "Outbox publish completed for {MessageType} on attempt {AttemptCount}.",
                item.MessageType, item.AttemptCount);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var failure = _classifier.Classify(exception);
            var now = _timeProvider.GetUtcNow();
            if (failure.Category == MessagePublishFailureCategory.Permanent
                || item.AttemptCount >= _options.MaxAttempts)
            {
                var code = item.AttemptCount >= _options.MaxAttempts
                    ? "MAX_ATTEMPTS_EXCEEDED"
                    : item.MessageType == 0
                        ? "UNKNOWN_MESSAGE_TYPE"
                        : failure.FailureCode;
                await WithStoreAsync(store => store.MarkDeadLetteredAsync(
                    item.MessageId, item.LockOwner, now, code,
                    cancellationToken));
            }
            else
            {
                var exponent = Math.Min(item.AttemptCount - 1, 30);
                var seconds = Math.Min(
                    _options.MaxRetryDelaySeconds,
                    _options.BaseRetryDelaySeconds * Math.Pow(2, exponent));
                await WithStoreAsync(store => store.ReleaseForRetryAsync(
                    item.MessageId, item.LockOwner,
                    now.AddSeconds(seconds), failure.FailureCode,
                    cancellationToken));
            }
            _logger.LogWarning(
                "Outbox publish failed for {MessageType} on attempt {AttemptCount}; category {FailureCategory}.",
                item.MessageType, item.AttemptCount, failure.Category);
        }
    }

    private async Task WithStoreAsync(Func<IOutboxStore, Task> operation)
    {
        using var scope = _scopeFactory.CreateScope();
        await operation(scope.ServiceProvider.GetRequiredService<IOutboxStore>());
    }
}
