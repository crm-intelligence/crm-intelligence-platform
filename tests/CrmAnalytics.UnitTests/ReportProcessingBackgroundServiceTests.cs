using System.Collections.Concurrent;
using CrmAnalytics.Application.Notifications;
using CrmAnalytics.Application.ReportProcessing;
using CrmAnalytics.Application.ReportRequests;
using CrmAnalytics.Contracts.Integrations;
using CrmAnalytics.Contracts.Notifications;
using CrmAnalytics.Domain.ReportRequests;
using CrmAnalytics.Infrastructure.ReportProcessing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CrmAnalytics.UnitTests;

public sealed class ReportProcessingBackgroundServiceTests
{
    [Fact]
    public async Task Worker_ProcessesQueuedItemWithScopedService()
    {
        var recorder = new ProcessingRecorder(expectedCallCount: 1);
        await using var provider = CreateServiceProvider(recorder);
        var queue = CreateQueue();
        using var worker = CreateWorker(provider, queue, enabled: true);

        await worker.StartAsync(CancellationToken.None);
        await queue.EnqueueAsync(
            CreateItem("request-1"),
            CancellationToken.None);
        await recorder.WaitForExpectedCallsAsync();
        await worker.StopAsync(CancellationToken.None);

        var call = Assert.Single(recorder.Calls);
        Assert.Equal("request-1", call.RequestId);
        Assert.NotSame(UserDataScope.Empty, call.UserDataScope);
        Assert.Empty(call.UserDataScope.Roles);
        Assert.Single(recorder.ProcessingServiceInstanceIds);
    }

    [Fact]
    public async Task Worker_EachItemUsesNewScopedProcessingService()
    {
        var recorder = new ProcessingRecorder(expectedCallCount: 2);
        await using var provider = CreateServiceProvider(recorder);
        var queue = CreateQueue(capacity: 2);
        using var worker = CreateWorker(provider, queue, enabled: true);

        await worker.StartAsync(CancellationToken.None);
        await queue.EnqueueAsync(
            CreateItem("request-1"),
            CancellationToken.None);
        await queue.EnqueueAsync(
            CreateItem("request-2"),
            CancellationToken.None);
        await recorder.WaitForExpectedCallsAsync();
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(2, recorder.Calls.Count);
        Assert.Equal(2, recorder.ProcessingServiceInstanceIds.Count);
    }

    [Fact]
    public async Task Worker_FirstUnexpectedFailureMarksFailedAndContinues()
    {
        var recorder = new ProcessingRecorder(
            expectedCallCount: 2,
            requestIdToThrow: "request-1");
        await using var provider = CreateServiceProvider(recorder);
        var queue = CreateQueue(capacity: 2);
        using var worker = CreateWorker(provider, queue, enabled: true);

        await worker.StartAsync(CancellationToken.None);
        await queue.EnqueueAsync(
            CreateItem("request-1"),
            CancellationToken.None);
        await queue.EnqueueAsync(
            CreateItem("request-2"),
            CancellationToken.None);
        await recorder.WaitForExpectedCallsAsync();
        await recorder.WaitForFailureAsync();
        await worker.StopAsync(CancellationToken.None);

        Assert.Collection(
            recorder.Calls,
            first => Assert.Equal("request-1", first.RequestId),
            second => Assert.Equal("request-2", second.RequestId));
        var failure = Assert.Single(recorder.Failures);
        Assert.Equal("request-1", failure.RequestId);
        Assert.Equal(
            "BACKGROUND_PROCESSING_FAILED",
            failure.ErrorCode);
        Assert.Equal(
            "Rapor işleme sırasında beklenmeyen bir hata oluştu.",
            failure.ErrorMessage);
        Assert.Equal(TimeSpan.Zero, failure.FailedAt.Offset);
    }

    [Fact]
    public async Task Worker_StoppingTokenCancellationStopsNormally()
    {
        var recorder = new ProcessingRecorder(expectedCallCount: 1);
        await using var provider = CreateServiceProvider(recorder);
        var queue = CreateQueue();
        using var worker = CreateWorker(provider, queue, enabled: true);

        await worker.StartAsync(CancellationToken.None);
        await worker.StopAsync(CancellationToken.None);

        Assert.Empty(recorder.Calls);
        Assert.Empty(recorder.Failures);
    }

    [Fact]
    public async Task Worker_DisabledDoesNotDequeueOrProcess()
    {
        var recorder = new ProcessingRecorder(expectedCallCount: 1);
        await using var provider = CreateServiceProvider(recorder);
        var queue = new CountingQueue();
        using var worker = CreateWorker(provider, queue, enabled: false);

        await worker.StartAsync(CancellationToken.None);
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(0, queue.DequeueCallCount);
        Assert.Empty(recorder.Calls);
    }

    [Theory]
    [InlineData(
        ReportRequestStatus.Completed,
        ReportNotificationStatus.Completed)]
    [InlineData(
        ReportRequestStatus.Failed,
        ReportNotificationStatus.Failed)]
    [InlineData(
        ReportRequestStatus.WaitingForClarification,
        ReportNotificationStatus.WaitingForClarification)]
    [InlineData(
        ReportRequestStatus.Rejected,
        ReportNotificationStatus.Rejected)]
    public async Task Worker_TerminalResultIsSentToNotifier(
        ReportRequestStatus resultStatus,
        ReportNotificationStatus notificationStatus)
    {
        var recorder = new ProcessingRecorder(
            expectedCallCount: 1,
            resultStatus: resultStatus,
            expectedNotificationCount: 1);
        await using var provider = CreateServiceProvider(recorder);
        var queue = CreateQueue();
        using var worker = CreateWorker(provider, queue, enabled: true);

        await worker.StartAsync(CancellationToken.None);
        await queue.EnqueueAsync(
            CreateItem("request-terminal"),
            CancellationToken.None);
        await recorder.WaitForNotificationsAsync();
        await worker.StopAsync(CancellationToken.None);

        var notification = Assert.Single(recorder.Notifications);
        Assert.Equal(notificationStatus, notification.Status);
        Assert.Equal("request-terminal", notification.RequestId);
        Assert.Equal(
            "correlation-request-terminal",
            notification.CorrelationId);
        Assert.Equal("summary", notification.Summary);
        Assert.Equal(
            "https://app.powerbi.com/report",
            notification.PowerBiUrl);
    }

    [Fact]
    public async Task Worker_NotificationFailureDoesNotFailReportAndContinues()
    {
        var recorder = new ProcessingRecorder(
            expectedCallCount: 2,
            notificationThrows: true,
            expectedNotificationCount: 2);
        await using var provider = CreateServiceProvider(recorder);
        var queue = CreateQueue(capacity: 2);
        using var worker = CreateWorker(provider, queue, enabled: true);

        await worker.StartAsync(CancellationToken.None);
        await queue.EnqueueAsync(
            CreateItem("request-1"),
            CancellationToken.None);
        await queue.EnqueueAsync(
            CreateItem("request-2"),
            CancellationToken.None);
        await recorder.WaitForNotificationsAsync();
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(2, recorder.Calls.Count);
        Assert.Equal(2, recorder.Notifications.Count);
        Assert.Empty(recorder.Failures);
    }

    [Fact]
    public async Task Worker_UnexpectedProcessingFailureNotifiesFailedResult()
    {
        var recorder = new ProcessingRecorder(
            expectedCallCount: 1,
            requestIdToThrow: "request-1",
            expectedNotificationCount: 1);
        await using var provider = CreateServiceProvider(recorder);
        var queue = CreateQueue();
        using var worker = CreateWorker(provider, queue, enabled: true);

        await worker.StartAsync(CancellationToken.None);
        await queue.EnqueueAsync(
            CreateItem("request-1"),
            CancellationToken.None);
        await recorder.WaitForFailureAsync();
        await recorder.WaitForNotificationsAsync();
        await worker.StopAsync(CancellationToken.None);

        var notification = Assert.Single(recorder.Notifications);
        Assert.Equal(ReportNotificationStatus.Failed, notification.Status);
        Assert.Equal(
            "BACKGROUND_PROCESSING_FAILED",
            notification.ErrorCode);
    }

    private static ServiceProvider CreateServiceProvider(
        ProcessingRecorder recorder)
    {
        var services = new ServiceCollection();
        services.AddSingleton(recorder);
        services.AddScoped<IReportProcessingService>(
            serviceProvider =>
                new RecordingProcessingService(
                    serviceProvider.GetRequiredService<
                        ProcessingRecorder>()));
        services.AddScoped<IReportRequestService>(
            serviceProvider =>
                new RecordingReportRequestService(
                    serviceProvider.GetRequiredService<
                        ProcessingRecorder>()));
        services.AddScoped<IReportStatusNotificationClient>(
            serviceProvider =>
                new RecordingNotificationClient(
                    serviceProvider.GetRequiredService<
                        ProcessingRecorder>()));
        return services.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateScopes = true,
                ValidateOnBuild = true
            });
    }

    private static ReportProcessingBackgroundService CreateWorker(
        IServiceProvider provider,
        IReportProcessingQueue queue,
        bool enabled)
    {
        return new ReportProcessingBackgroundService(
            queue,
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(
                new ReportProcessingQueueOptions
                {
                    Enabled = enabled,
                    Capacity = 100
                }),
            NullLogger<ReportProcessingBackgroundService>.Instance);
    }

    private static ChannelReportProcessingQueue CreateQueue(
        int capacity = 1)
    {
        return new ChannelReportProcessingQueue(
            Options.Create(
                new ReportProcessingQueueOptions
                {
                    Enabled = true,
                    Capacity = capacity
                }));
    }

    private static QueuedReportProcessingRequest CreateItem(
        string requestId)
    {
        return new QueuedReportProcessingRequest(
            requestId,
            $"correlation-{requestId}",
            UserDataScope.Empty,
            DateTimeOffset.UtcNow);
    }

    private sealed class ProcessingRecorder
    {
        private readonly int _expectedCallCount;
        private readonly string? _requestIdToThrow;
        private readonly ReportRequestStatus _resultStatus;
        private readonly bool _notificationThrows;
        private readonly int _expectedNotificationCount;
        private readonly TaskCompletionSource _callsCompleted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _failureCompleted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _notificationsCompleted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ProcessingRecorder(
            int expectedCallCount,
            string? requestIdToThrow = null,
            ReportRequestStatus resultStatus =
                ReportRequestStatus.Completed,
            bool notificationThrows = false,
            int expectedNotificationCount = 0)
        {
            _expectedCallCount = expectedCallCount;
            _requestIdToThrow = requestIdToThrow;
            _resultStatus = resultStatus;
            _notificationThrows = notificationThrows;
            _expectedNotificationCount = expectedNotificationCount;
        }

        public ConcurrentQueue<ProcessReportRequestCommand> Calls { get; } =
            new();

        public ConcurrentDictionary<Guid, byte>
            ProcessingServiceInstanceIds { get; } = new();

        public ConcurrentQueue<FailReportRequestCommand> Failures { get; } =
            new();

        public ConcurrentQueue<ReportStatusNotificationRequest>
            Notifications { get; } = new();

        public ReportRequestStatus ResultStatus => _resultStatus;

        public async Task RecordProcessingAsync(
            Guid instanceId,
            ProcessReportRequestCommand command)
        {
            ProcessingServiceInstanceIds.TryAdd(instanceId, 0);
            Calls.Enqueue(command);

            if (Calls.Count >= _expectedCallCount)
            {
                _callsCompleted.TrySetResult();
            }

            await Task.Yield();

            if (string.Equals(
                command.RequestId,
                _requestIdToThrow,
                StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "unexpected processing failure");
            }
        }

        public void RecordFailure(FailReportRequestCommand command)
        {
            Failures.Enqueue(command);
            _failureCompleted.TrySetResult();
        }

        public void RecordNotification(
            ReportStatusNotificationRequest notification)
        {
            Notifications.Enqueue(notification);
            if (Notifications.Count >= _expectedNotificationCount)
            {
                _notificationsCompleted.TrySetResult();
            }

            if (_notificationThrows)
            {
                throw new InvalidOperationException(
                    "notification failure");
            }
        }

        public Task WaitForExpectedCallsAsync() =>
            _callsCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        public Task WaitForFailureAsync() =>
            _failureCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        public Task WaitForNotificationsAsync() =>
            _notificationsCompleted.Task.WaitAsync(
                TimeSpan.FromSeconds(5));
    }

    private sealed class RecordingProcessingService
        : IReportProcessingService
    {
        private readonly ProcessingRecorder _recorder;
        private readonly Guid _instanceId = Guid.NewGuid();

        public RecordingProcessingService(ProcessingRecorder recorder)
        {
            _recorder = recorder;
        }

        public async Task<ProcessReportRequestResult> ProcessAsync(
            ProcessReportRequestCommand command,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _recorder.RecordProcessingAsync(_instanceId, command);
            return new ProcessReportRequestResult(
                command.RequestId,
                _recorder.ResultStatus,
                DateTimeOffset.UtcNow,
                "summary",
                "https://app.powerbi.com/report",
                _recorder.ResultStatus
                    == ReportRequestStatus.WaitingForClarification
                    ? "Which period?"
                    : null,
                _recorder.ResultStatus == ReportRequestStatus.Failed
                    ? "PROCESSING_FAILED"
                    : null,
                _recorder.ResultStatus == ReportRequestStatus.Rejected
                    ? "GR007"
                    : null,
                _recorder.ResultStatus == ReportRequestStatus.Rejected
                    ? "Bu kapsam desteklenmiyor."
                    : null);
        }
    }

    private sealed class RecordingReportRequestService
        : IReportRequestService
    {
        private readonly ProcessingRecorder _recorder;

        public RecordingReportRequestService(ProcessingRecorder recorder)
        {
            _recorder = recorder;
        }

        public Task<UpdateReportRequestResult> FailAsync(
            FailReportRequestCommand command,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _recorder.RecordFailure(command);
            return Task.FromResult(
                new UpdateReportRequestResult(
                    command.RequestId,
                    ReportRequestStatus.Failed,
                    command.FailedAt));
        }

        public Task<CreateReportRequestResult> CreateAsync(
            CreateReportRequestCommand command,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ReviseReportRequestResult> ReviseAsync(
            ReviseReportRequestCommand command,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<GetReportRequestResult?> GetByIdAsync(
            string requestId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var failure = _recorder.Failures.FirstOrDefault(
                item => string.Equals(
                    item.RequestId,
                    requestId,
                    StringComparison.Ordinal));
            return Task.FromResult(
                failure is null
                    ? null
                    : new GetReportRequestResult(
                        requestId,
                        "conversation-redacted",
                        null,
                        ReportRequestStatus.Failed,
                        failure.FailedAt,
                        failure.FailedAt,
                        $"correlation-{requestId}",
                        null,
                        null,
                        null,
                        null,
                        failure.ErrorCode,
                        failure.ErrorMessage));
        }

        public Task<UpdateReportRequestResult> TransitionStatusAsync(
            TransitionReportRequestStatusCommand command,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<UpdateReportRequestResult> CompleteAsync(
            CompleteReportRequestCommand command,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<UpdateReportRequestResult> RequestClarificationAsync(
            RequestReportClarificationCommand command,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<UpdateReportRequestResult> SubmitClarificationAsync(
            SubmitReportClarificationCommand command,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingNotificationClient
        : IReportStatusNotificationClient
    {
        private readonly ProcessingRecorder _recorder;

        public RecordingNotificationClient(ProcessingRecorder recorder)
        {
            _recorder = recorder;
        }

        public Task NotifyAsync(
            ReportStatusNotificationRequest notification,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _recorder.RecordNotification(notification);
            return Task.CompletedTask;
        }
    }

    private sealed class CountingQueue : IReportProcessingQueue
    {
        public int DequeueCallCount { get; private set; }

        public ValueTask EnqueueAsync(
            QueuedReportProcessingRequest request,
            CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask<QueuedReportProcessingRequest> DequeueAsync(
            CancellationToken cancellationToken)
        {
            DequeueCallCount++;
            return ValueTask.FromCanceled<QueuedReportProcessingRequest>(
                cancellationToken);
        }
    }
}
