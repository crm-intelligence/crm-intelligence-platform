extern alias TeamsHost;

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CrmAnalytics.Contracts.Notifications;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Teams.Cards;
using ReportNotificationCard =
    TeamsHost::CrmAnalytics.Teams.Notifications.ReportNotificationCard;
using ITeamsNotificationTargetStore =
    TeamsHost::CrmAnalytics.Teams.Notifications.ITeamsNotificationTargetStore;
using ITeamsProactiveNotificationSender =
    TeamsHost::CrmAnalytics.Teams.Notifications.ITeamsProactiveNotificationSender;
using TeamsHostMarker =
    TeamsHost::CrmAnalytics.Teams.TeamsHostMarker;
using TeamsNotificationTarget =
    TeamsHost::CrmAnalytics.Teams.Notifications.TeamsNotificationTarget;

namespace CrmAnalytics.IntegrationTests;

public sealed class TeamsNotificationEndpointTests
{
    private const string ApiKey = "integration-test-notification-key";

    [Fact]
    public async Task Disabled_ReturnsNotFound()
    {
        await using var factory = new TeamsNotificationFactory(
            enabled: false);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            ReportNotificationHttpConstants.CallbackPath,
            CreateNotification());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong-key")]
    public async Task MissingOrWrongKey_ReturnsUnauthorized(
        string? suppliedKey)
    {
        await using var factory = new TeamsNotificationFactory(
            enabled: true);
        using var client = factory.CreateClient();
        using var request = CreateRequest(
            CreateNotification(),
            suppliedKey);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task MissingMapping_ReturnsTargetNotReadyConflict()
    {
        await using var factory = new TeamsNotificationFactory(
            enabled: true);
        using var client = factory.CreateClient();
        using var request = CreateRequest(
            CreateNotification(),
            ApiKey);

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains(
            ReportNotificationHttpConstants.TargetNotReadyErrorCode,
            body);
    }

    [Fact]
    public async Task ValidCallback_SendsOnceToStoredConversation()
    {
        var sender = new RecordingSender();
        await using var factory = new TeamsNotificationFactory(
            enabled: true,
            sender);
        using var client = factory.CreateClient();
        var requestId = $"request-{Guid.NewGuid():N}";
        var conversationId = $"conversation-{Guid.NewGuid():N}";
        var notification = CreateNotification(requestId);
        await SaveTargetAsync(
            factory,
            requestId,
            conversationId);

        using var firstRequest = CreateRequest(notification, ApiKey);
        using var firstResponse = await client.SendAsync(firstRequest);
        using var secondRequest = CreateRequest(notification, ApiKey);
        using var secondResponse = await client.SendAsync(secondRequest);

        Assert.Equal(
            HttpStatusCode.NoContent,
            firstResponse.StatusCode);
        Assert.Equal(
            HttpStatusCode.NoContent,
            secondResponse.StatusCode);
        var delivery = Assert.Single(sender.Deliveries);
        Assert.Equal(conversationId, delivery.ConversationId);
        Assert.Equal("AdaptiveCard", delivery.Card.Card.Type);
        var visibleContent = GetVisibleContent(delivery.Card);
        Assert.Contains("Raporunuz hazır", visibleContent);
        Assert.Contains("Güvenli özet", visibleContent);
        Assert.Contains(requestId, visibleContent);
        var action = Assert.Single(
            delivery.Card.Card.Actions!.OfType<OpenUrlAction>());
        Assert.Equal(
            "https://app.powerbi.com/report/42",
            action.Url);
        Assert.DoesNotContain(
            "secret-prompt-value",
            JsonSerializer.Serialize(delivery.Card.Card),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WaitingForClarificationCallback_SendsQuestionCard()
    {
        var sender = new RecordingSender();
        await using var factory = new TeamsNotificationFactory(
            enabled: true,
            sender);
        using var client = factory.CreateClient();
        var requestId = $"request-{Guid.NewGuid():N}";
        await SaveTargetAsync(
            factory,
            requestId,
            $"conversation-{Guid.NewGuid():N}");
        var notification = CreateNotification(requestId) with
        {
            Status =
                ReportNotificationStatus.WaitingForClarification,
            Summary = null,
            PowerBiUrl = null,
            ClarificationQuestion = "Hangi dönem?"
        };
        using var request = CreateRequest(notification, ApiKey);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var delivery = Assert.Single(sender.Deliveries);
        var content = GetVisibleContent(delivery.Card);
        Assert.Contains("Ek bilgi gerekiyor", content);
        Assert.Contains("Açıklama bekleniyor", content);
        Assert.Contains("Hangi dönem?", content);
        Assert.Single(
            delivery.Card.Card.Actions!.OfType<ExecuteAction>());
    }

    [Fact]
    public async Task FailedCallback_OmitsErrorCodeFromCard()
    {
        const string errorCode = "SECRET_INTERNAL_FAILURE";
        var sender = new RecordingSender();
        await using var factory = new TeamsNotificationFactory(
            enabled: true,
            sender);
        using var client = factory.CreateClient();
        var requestId = $"request-{Guid.NewGuid():N}";
        await SaveTargetAsync(
            factory,
            requestId,
            $"conversation-{Guid.NewGuid():N}");
        var notification = CreateNotification(requestId) with
        {
            Status = ReportNotificationStatus.Failed,
            Summary = null,
            PowerBiUrl = null,
            ErrorCode = errorCode
        };
        using var request = CreateRequest(notification, ApiKey);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var delivery = Assert.Single(sender.Deliveries);
        Assert.Contains(
            "Rapor talebi şu anda tamamlanamadı.",
            GetVisibleContent(delivery.Card));
        Assert.DoesNotContain(
            errorCode,
            JsonSerializer.Serialize(delivery.Card.Card),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvalidPowerBiUrl_StillSendsCompletedCardWithoutAction()
    {
        var sender = new RecordingSender();
        await using var factory = new TeamsNotificationFactory(
            enabled: true,
            sender);
        using var client = factory.CreateClient();
        var requestId = $"request-{Guid.NewGuid():N}";
        await SaveTargetAsync(
            factory,
            requestId,
            $"conversation-{Guid.NewGuid():N}");
        var notification = CreateNotification(requestId) with
        {
            PowerBiUrl = "not-a-url"
        };
        using var request = CreateRequest(notification, ApiKey);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var delivery = Assert.Single(sender.Deliveries);
        Assert.Contains(
            "Güvenli özet",
            GetVisibleContent(delivery.Card));
        Assert.Empty(
            delivery.Card.Card.Actions!.OfType<OpenUrlAction>());
        Assert.Single(
            delivery.Card.Card.Actions!.OfType<ExecuteAction>());
    }

    [Fact]
    public async Task SenderFailure_ReturnsSafeServiceError()
    {
        const string technicalMessage =
            "connector secret technical exception";
        var sender = new RecordingSender
        {
            Exception = new InvalidOperationException(technicalMessage)
        };
        await using var factory = new TeamsNotificationFactory(
            enabled: true,
            sender);
        using var client = factory.CreateClient();
        var requestId = $"request-{Guid.NewGuid():N}";
        await SaveTargetAsync(
            factory,
            requestId,
            $"conversation-{Guid.NewGuid():N}");
        using var request = CreateRequest(
            CreateNotification(requestId),
            ApiKey);

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(
            HttpStatusCode.ServiceUnavailable,
            response.StatusCode);
        Assert.DoesNotContain(
            technicalMessage,
            body,
            StringComparison.Ordinal);
        Assert.Contains("NOTIFICATION_DELIVERY_FAILED", body);
    }

    [Theory]
    [InlineData(999)]
    [InlineData(-1)]
    public async Task InvalidStatus_ReturnsBadRequest(int status)
    {
        await using var factory = new TeamsNotificationFactory(
            enabled: true);
        using var client = factory.CreateClient();
        var requestId = $"request-{Guid.NewGuid():N}";
        await SaveTargetAsync(
            factory,
            requestId,
            $"conversation-{Guid.NewGuid():N}");
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            ReportNotificationHttpConstants.CallbackPath)
        {
            Content = JsonContent.Create(
                new
                {
                    requestId,
                    status,
                    updatedAt = DateTimeOffset.UtcNow,
                    correlationId = $"correlation-{Guid.NewGuid():N}"
                })
        };
        request.Headers.Add(
            ReportNotificationHttpConstants.ApiKeyHeaderName,
            ApiKey);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static HttpRequestMessage CreateRequest(
        ReportStatusNotificationRequest notification,
        string? apiKey)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            ReportNotificationHttpConstants.CallbackPath)
        {
            Content = JsonContent.Create(notification)
        };
        if (apiKey is not null)
        {
            request.Headers.Add(
                ReportNotificationHttpConstants.ApiKeyHeaderName,
                apiKey);
        }

        return request;
    }

    private static ReportStatusNotificationRequest CreateNotification(
        string? requestId = null) =>
        new()
        {
            RequestId = requestId ?? $"request-{Guid.NewGuid():N}",
            Status = ReportNotificationStatus.Completed,
            UpdatedAt = DateTimeOffset.UtcNow,
            CorrelationId = $"correlation-{Guid.NewGuid():N}",
            Summary = "Güvenli özet",
            PowerBiUrl = "https://app.powerbi.com/report/42"
        };

    private static async Task SaveTargetAsync(
        TeamsNotificationFactory factory,
        string requestId,
        string conversationId)
    {
        var store = factory.Services
            .GetRequiredService<ITeamsNotificationTargetStore>();
        await store.SaveAsync(
            new TeamsNotificationTarget(
                requestId,
                conversationId,
                DateTimeOffset.UtcNow),
            CancellationToken.None);
    }

    private sealed class TeamsNotificationFactory
        : WebApplicationFactory<TeamsHostMarker>
    {
        private readonly bool _enabled;
        private readonly RecordingSender _sender;

        public TeamsNotificationFactory(
            bool enabled,
            RecordingSender? sender = null)
        {
            _enabled = enabled;
            _sender = sender ?? new RecordingSender();
        }

        protected override void ConfigureWebHost(
            IWebHostBuilder builder)
        {
            builder.UseEnvironment(Environments.Development);
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["BackendApi:BaseUrl"] =
                            "https://localhost:7090",
                        ["BackendApi:TimeoutSeconds"] = "15",
                        ["Teams:SkipAuth"] = "true",
                        ["ReportNotifications:Enabled"] =
                            _enabled.ToString(),
                        ["ReportNotifications:ApiKey"] =
                            _enabled ? ApiKey : null
                    });
            });
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<
                    ITeamsProactiveNotificationSender>();
                services.AddSingleton<
                    ITeamsProactiveNotificationSender>(_sender);
            });
        }
    }

    private sealed class RecordingSender
        : ITeamsProactiveNotificationSender
    {
        public ConcurrentQueue<Delivery> Deliveries { get; } = new();

        public Exception? Exception { get; init; }

        public Task SendCardAsync(
            string conversationId,
            ReportNotificationCard card,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
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

    private static string GetVisibleContent(
        ReportNotificationCard card)
    {
        var text = card.Card.Body!
            .OfType<TextBlock>()
            .Select(block => block.Text ?? string.Empty);
        var facts = card.Card.Body!
            .OfType<FactSet>()
            .SelectMany(factSet => factSet.Facts ?? [])
            .Select(fact => $"{fact.Title}: {fact.Value}");

        return string.Join(Environment.NewLine, text.Concat(facts));
    }
}
