using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using CrmAnalytics.Application.Notifications;
using CrmAnalytics.Contracts.Notifications;
using CrmAnalytics.Contracts.ReportRequests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrmAnalytics.IntegrationTests;

public sealed class ReportNotificationWorkerIntegrationTests
{
    [Fact]
    public async Task CompletedWorkerResult_SendsExpectedPayload()
    {
        var notifier = new RecordingNotificationClient();
        await using var factory =
            new NotificationApiFactory(notifier);
        using var client = factory.CreateClient();
        var created = await CreateAsync(client);

        var notification = await notifier.WaitAsync();
        var completed = await WaitForCompletedAsync(
            client,
            created.RequestId);

        Assert.Equal(created.RequestId, notification.RequestId);
        Assert.Equal(
            ReportNotificationStatus.Completed,
            notification.Status);
        Assert.Equal(completed.CorrelationId, notification.CorrelationId);
        Assert.Equal(completed.Summary, notification.Summary);
        Assert.Equal(completed.PowerBiUrl, notification.PowerBiUrl);
        Assert.Equal(TimeSpan.Zero, notification.UpdatedAt.Offset);
    }

    [Fact]
    public async Task NotificationFailure_DoesNotChangeCompletedStatus()
    {
        var notifier = new RecordingNotificationClient
        {
            Exception = new InvalidOperationException(
                "notification transport failure")
        };
        await using var factory =
            new NotificationApiFactory(notifier);
        using var client = factory.CreateClient();
        var created = await CreateAsync(client);

        await notifier.WaitAsync();
        var completed = await WaitForCompletedAsync(
            client,
            created.RequestId);

        Assert.Equal("Completed", completed.Status);
        Assert.Null(completed.ErrorCode);
        Assert.Null(completed.ErrorMessage);
    }

    private static async Task<CreateReportRequestResponse> CreateAsync(
        HttpClient client)
    {
        var response = await client.PostAsJsonAsync(
            "/api/report-requests",
            new CreateReportRequestRequest
            {
                Prompt = "Create a regional sales report.",
                ConversationId =
                    $"conversation-{Guid.NewGuid():N}"
            });
        var created = await response.Content
            .ReadFromJsonAsync<CreateReportRequestResponse>();

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return Assert.IsType<CreateReportRequestResponse>(created);
    }

    private static async Task<GetReportRequestResponse>
        WaitForCompletedAsync(
            HttpClient client,
            string requestId)
    {
        using var timeout = new CancellationTokenSource(
            TimeSpan.FromSeconds(10));

        while (true)
        {
            var report = await client.GetFromJsonAsync<
                GetReportRequestResponse>(
                    $"/api/report-requests/{requestId}",
                    timeout.Token);
            Assert.NotNull(report);

            if (report.Status == "Completed")
            {
                return report;
            }

            await Task.Delay(20, timeout.Token);
        }
    }

    private sealed class NotificationApiFactory
        : WebApplicationFactory<Program>
    {
        private readonly RecordingNotificationClient _notifier;

        public NotificationApiFactory(
            RecordingNotificationClient notifier)
        {
            _notifier = notifier;
        }

        protected override void ConfigureWebHost(
            IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["ReportProcessing:Queue:Enabled"] = "true",
                        ["ReportProcessing:Queue:Capacity"] = "100",
                        ["ExternalServices:Mocks:DelayMilliseconds"] = "0",
                        ["TeamsNotifications:Enabled"] = "false"
                    });
            });
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IReportStatusNotificationClient>();
                services.AddSingleton<IReportStatusNotificationClient>(
                    _notifier);
            });
        }
    }

    private sealed class RecordingNotificationClient
        : IReportStatusNotificationClient
    {
        private readonly ConcurrentQueue<
            ReportStatusNotificationRequest> _notifications = new();
        private readonly TaskCompletionSource<
            ReportStatusNotificationRequest> _firstNotification =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Exception? Exception { get; init; }

        public Task NotifyAsync(
            ReportStatusNotificationRequest notification,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _notifications.Enqueue(notification);
            _firstNotification.TrySetResult(notification);

            return Exception is null
                ? Task.CompletedTask
                : Task.FromException(Exception);
        }

        public Task<ReportStatusNotificationRequest> WaitAsync() =>
            _firstNotification.Task.WaitAsync(
                TimeSpan.FromSeconds(10));
    }
}
