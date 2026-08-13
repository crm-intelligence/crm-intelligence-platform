using System.Collections.Concurrent;
using System.Text.Json;
using CrmAnalytics.Contracts.Notifications;
using CrmAnalytics.Teams.Configuration;
using CrmAnalytics.Teams.Hosting;
using CrmAnalytics.Teams.Notifications;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Teams.Cards;

namespace CrmAnalytics.UnitTests;

public sealed class ReportNotificationEndpointTests
{
    private const string ApiKey = "unit-endpoint-key";

    [Fact]
    public async Task Disabled_Returns404()
    {
        var result = await InvokeAsync(enabled: false);

        AssertStatus(result, StatusCodes.Status404NotFound);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong")]
    public async Task MissingOrWrongKey_Returns401(string? apiKey)
    {
        var result = await InvokeAsync(apiKey: apiKey);

        AssertStatus(result, StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task InvalidBody_Returns400()
    {
        var result = await InvokeAsync(
            notification: CreateNotification() with
            {
                CorrelationId = " "
            });

        AssertStatus(result, StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task MissingMapping_ReturnsTargetNotReady409()
    {
        var result = await InvokeAsync();

        AssertStatus(result, StatusCodes.Status409Conflict);
        var body = JsonSerializer.Serialize(
            Assert.IsAssignableFrom<IValueHttpResult>(result).Value);
        Assert.Contains(
            ReportNotificationHttpConstants.TargetNotReadyErrorCode,
            body);
    }

    [Fact]
    public async Task ValidCallback_UsesStoredConversationAndReturns204()
    {
        var sender = new RecordingSender();
        var notification = CreateNotification();
        var targetStore = await CreateTargetStoreAsync(
            notification.RequestId,
            "conversation-42");

        var result = await InvokeAsync(
            notification: notification,
            targetStore: targetStore,
            sender: sender);

        AssertStatus(result, StatusCodes.Status204NoContent);
        var delivery = Assert.Single(sender.Deliveries);
        Assert.Equal("conversation-42", delivery.ConversationId);
        Assert.Contains(
            notification.RequestId,
            JsonSerializer.Serialize(delivery.Card.Card));
    }

    [Fact]
    public async Task CompletedCallback_SendsCardWithPowerBiAction()
    {
        var sender = new RecordingSender();
        var notification = CreateNotification() with
        {
            PowerBiUrl = "https://app.powerbi.com/report/42"
        };
        var targetStore = await CreateTargetStoreAsync(
            notification.RequestId,
            "conversation-42");

        var result = await InvokeAsync(
            notification: notification,
            targetStore: targetStore,
            sender: sender);

        AssertStatus(result, StatusCodes.Status204NoContent);
        var action = Assert.Single(
            Assert.Single(sender.Deliveries)
                .Card.Card.Actions!
                .OfType<OpenUrlAction>());
        Assert.Equal(
            "https://app.powerbi.com/report/42",
            action.Url);
    }

    [Fact]
    public async Task WaitingForClarificationCallback_SendsQuestionCard()
    {
        var sender = new RecordingSender();
        var notification = CreateNotification() with
        {
            Status =
                ReportNotificationStatus.WaitingForClarification,
            ClarificationQuestion = "Hangi dönem?"
        };
        var targetStore = await CreateTargetStoreAsync(
            notification.RequestId,
            "conversation-42");

        var result = await InvokeAsync(
            notification: notification,
            targetStore: targetStore,
            sender: sender);

        AssertStatus(result, StatusCodes.Status204NoContent);
        var serialized = JsonSerializer.Serialize(
            Assert.Single(sender.Deliveries).Card.Card);
        Assert.Contains("Hangi d\\u00F6nem?", serialized);
        Assert.DoesNotContain(
            "Action.Submit",
            serialized,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailedCallback_SendsSafeFailedCard()
    {
        var sender = new RecordingSender();
        var notification = CreateNotification() with
        {
            Status = ReportNotificationStatus.Failed,
            ErrorCode = "SECRET_INTERNAL_ERROR"
        };
        var targetStore = await CreateTargetStoreAsync(
            notification.RequestId,
            "conversation-42");

        var result = await InvokeAsync(
            notification: notification,
            targetStore: targetStore,
            sender: sender);

        AssertStatus(result, StatusCodes.Status204NoContent);
        var serialized = JsonSerializer.Serialize(
            Assert.Single(sender.Deliveries).Card.Card);
        Assert.Contains(
            "Rapor talebi \\u015Fu anda tamamlanamad\\u0131.",
            serialized);
        Assert.DoesNotContain(
            notification.ErrorCode,
            serialized,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task DuplicateCallback_DoesNotSendTwice()
    {
        var sender = new RecordingSender();
        var notification = CreateNotification();
        var targetStore = await CreateTargetStoreAsync(
            notification.RequestId,
            "conversation-42");
        var deliveryStore =
            new InMemoryReportNotificationDeliveryStore();

        var first = await InvokeAsync(
            notification: notification,
            targetStore: targetStore,
            deliveryStore: deliveryStore,
            sender: sender);
        var second = await InvokeAsync(
            notification: notification,
            targetStore: targetStore,
            deliveryStore: deliveryStore,
            sender: sender);

        AssertStatus(first, StatusCodes.Status204NoContent);
        AssertStatus(second, StatusCodes.Status204NoContent);
        Assert.Single(sender.Deliveries);
    }

    [Fact]
    public async Task SenderFailure_ReturnsSafe503AndCanBeRetried()
    {
        const string technicalMessage =
            "secret connector exception details";
        var sender = new RecordingSender
        {
            Exception = new InvalidOperationException(technicalMessage)
        };
        var notification = CreateNotification();
        var targetStore = await CreateTargetStoreAsync(
            notification.RequestId,
            "conversation-42");
        var deliveryStore =
            new InMemoryReportNotificationDeliveryStore();

        var first = await InvokeAsync(
            notification: notification,
            targetStore: targetStore,
            deliveryStore: deliveryStore,
            sender: sender);
        var second = await InvokeAsync(
            notification: notification,
            targetStore: targetStore,
            deliveryStore: deliveryStore,
            sender: sender);

        AssertStatus(
            first,
            StatusCodes.Status503ServiceUnavailable);
        AssertStatus(
            second,
            StatusCodes.Status503ServiceUnavailable);
        Assert.Equal(2, sender.AttemptCount);
        var body = JsonSerializer.Serialize(
            Assert.IsAssignableFrom<IValueHttpResult>(first).Value);
        Assert.DoesNotContain(technicalMessage, body);
    }

    private static async Task<IResult> InvokeAsync(
        bool enabled = true,
        string? apiKey = ApiKey,
        ReportStatusNotificationRequest? notification = null,
        ITeamsNotificationTargetStore? targetStore = null,
        IReportNotificationDeliveryStore? deliveryStore = null,
        RecordingSender? sender = null)
    {
        var context = new DefaultHttpContext();
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(
            JsonSerializer.SerializeToUtf8Bytes(
                notification ?? CreateNotification(),
                new JsonSerializerOptions(
                    JsonSerializerDefaults.Web)));
        if (apiKey is not null)
        {
            context.Request.Headers[
                ReportNotificationHttpConstants.ApiKeyHeaderName] =
                apiKey;
        }

        return await ReportNotificationEndpointExtensions.HandleAsync(
            context,
            Options.Create(
                new ReportNotificationEndpointOptions
                {
                    Enabled = enabled,
                    ApiKey = ApiKey
                }),
            targetStore
                ?? new InMemoryTeamsNotificationTargetStore(),
            deliveryStore
                ?? new InMemoryReportNotificationDeliveryStore(),
            new ReportNotificationCardFactory(),
            sender ?? new RecordingSender(),
            NullLoggerFactory.Instance);
    }

    private static async Task<ITeamsNotificationTargetStore>
        CreateTargetStoreAsync(
            string requestId,
            string conversationId)
    {
        var store = new InMemoryTeamsNotificationTargetStore();
        await store.SaveAsync(
            new TeamsNotificationTarget(
                requestId,
                conversationId,
                DateTimeOffset.UtcNow),
            CancellationToken.None);
        return store;
    }

    private static ReportStatusNotificationRequest CreateNotification() =>
        new()
        {
            RequestId = $"request-{Guid.NewGuid():N}",
            Status = ReportNotificationStatus.Completed,
            UpdatedAt = DateTimeOffset.UtcNow,
            CorrelationId = $"correlation-{Guid.NewGuid():N}",
            Summary = "Safe summary"
        };

    private static void AssertStatus(IResult result, int expected)
    {
        Assert.Equal(
            expected,
            Assert.IsAssignableFrom<IStatusCodeHttpResult>(result)
                .StatusCode);
    }

    private sealed class RecordingSender
        : ITeamsProactiveNotificationSender
    {
        public ConcurrentQueue<Delivery> Deliveries { get; } = new();

        public Exception? Exception { get; init; }

        public int AttemptCount { get; private set; }

        public Task SendCardAsync(
            string conversationId,
            ReportNotificationCard card,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AttemptCount++;

            if (Exception is not null)
            {
                return Task.FromException(Exception);
            }

            Deliveries.Enqueue(new Delivery(conversationId, card));
            return Task.CompletedTask;
        }
    }

    private sealed record Delivery(
        string ConversationId,
        ReportNotificationCard Card);
}
