using Azure.Messaging.ServiceBus;
using CrmAnalytics.Application.Conversations;
using CrmAnalytics.Application.Outbox;
using CrmAnalytics.Application.ReportProcessing;
using CrmAnalytics.Application.ReportRequests;
using CrmAnalytics.Application.Identity;
using CrmAnalytics.Domain.ReportRequests;
using CrmAnalytics.Infrastructure.Auditing;
using CrmAnalytics.Infrastructure.Messaging;
using CrmAnalytics.Infrastructure.Outbox;
using CrmAnalytics.Infrastructure.Persistence;
using CrmAnalytics.Infrastructure.ReportProcessing;
using CrmAnalytics.Contracts.Integrations;
using CrmAnalytics.Contracts.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CrmAnalytics.UnitTests;

public sealed class TransactionalOutboxTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 31, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AgenticRoutedCreation_PersistsBindingWithoutProcessingOutbox()
    {
        var reports = new InMemoryReportRequestRepository();
        var outbox = new InMemoryOutboxStore();
        var service = CreateReportService(reports, outbox);
        var user = TestUser();

        var result = await service.CreatePlannedAwaitingAgenticAsync(
            new CreateReportRequestCommand(
                "Prompt", "agentic-conversation", null, "correlation"),
            user,
            SubmittedPlan(),
            "{}",
            CancellationToken.None);

        Assert.Equal(ReportRequestStatus.Processing, result.Status);
        Assert.DoesNotContain(outbox.Snapshot, item =>
            item.Message.MessageType
                == OutboxMessageType.ReportProcessingRequested);
        var persisted = await reports.GetByIdAsync(
            result.RequestId, CancellationToken.None);
        Assert.Equal("{}", persisted?.CanonicalRequestJson);
    }

    [Fact]
    public async Task UnsupportedRoutedCreation_RejectsWithoutProcessingOutbox()
    {
        var reports = new InMemoryReportRequestRepository();
        var outbox = new InMemoryOutboxStore();
        var service = CreateReportService(reports, outbox);

        var result = await service.CreateRejectedPlannedAsync(
            new CreateReportRequestCommand(
                "Prompt", "unsupported-conversation", null, "correlation"),
            TestUser(),
            SubmittedPlan(),
            "UNKNOWN_METRIC",
            "The submitted analytical intent is not supported.",
            CancellationToken.None);

        Assert.Equal(ReportRequestStatus.Rejected, result.Status);
        Assert.DoesNotContain(outbox.Snapshot, item =>
            item.Message.MessageType
                == OutboxMessageType.ReportProcessingRequested);
    }

    [Fact]
    public async Task CompletedReport_WritesBoundedPreviewToDurableOutbox()
    {
        var reports = new InMemoryReportRequestRepository();
        var outbox = new InMemoryOutboxStore();
        var serializer = new OutboxMessageSerializer();
        var service = new ReportRequestService(
            reports,
            new ConversationContextService(
                new InMemoryConversationRepository(), reports),
            new InMemoryApplicationTransactionRunner(),
            new InMemoryApplicationAuditWriter(),
            outbox,
            new OutboxMessageFactory(serializer));
        var created = await service.CreateAsync(
            new CreateReportRequestCommand(
                "Prompt", "conversation", null, "correlation"),
            CancellationToken.None);
        var createdReport = (await reports.GetByIdAsync(
            created.RequestId, CancellationToken.None))!;
        var transitionAt = createdReport.UpdatedAt;
        await service.TransitionStatusAsync(
            new TransitionReportRequestStatusCommand(
                created.RequestId, ReportRequestStatus.Validating,
                transitionAt.AddTicks(1)), CancellationToken.None);
        await service.TransitionStatusAsync(
            new TransitionReportRequestStatusCommand(
                created.RequestId, ReportRequestStatus.Processing,
                transitionAt.AddTicks(2)), CancellationToken.None);
        var preview = new ReportVisualizationPreview
        {
            Kind = ReportVisualizationKinds.Kpi,
            Title = "Sipariş Sayısı",
            ValueLabel = "Sipariş Sayısı",
            DataPoints = [new() { Category = "", Value = 8 }],
            TotalRowCount = 1
        };

        await service.CompleteAsync(new CompleteReportRequestCommand(
            created.RequestId, "report", "summary",
            "https://app.powerbi.com/report", transitionAt.AddTicks(3),
            preview),
            CancellationToken.None);

        var message = Assert.Single(outbox.Snapshot, item =>
            item.Message.MessageType
                == OutboxMessageType.ReportNotificationRequested);
        var envelope = serializer.DeserializeReportNotificationRequested(
            message.Message.PayloadJson);
        Assert.NotNull(envelope.VisualizationPreview);
        Assert.Equal(preview.Kind, envelope.VisualizationPreview.Kind);
        Assert.Equal(preview.TotalRowCount,
            envelope.VisualizationPreview.TotalRowCount);
        Assert.Equal(preview.DataPoints,
            envelope.VisualizationPreview.DataPoints);
    }

    private static ReportRequestService CreateReportService(
        InMemoryReportRequestRepository reports,
        InMemoryOutboxStore outbox) => new(
        reports,
        new ConversationContextService(
            new InMemoryConversationRepository(), reports),
        new InMemoryApplicationTransactionRunner(),
        new InMemoryApplicationAuditWriter(),
        outbox,
        new OutboxMessageFactory(new OutboxMessageSerializer()));

    private static AuthenticatedUserContext TestUser() => new(
        "11111111-1111-4111-8111-111111111111",
        "22222222-2222-4222-8222-222222222222",
        ["Report.User"]);

    private static SubmittedSemanticPlanningResult SubmittedPlan() => new(
        "accepted",
        new SubmittedSemanticIntent(
            "order_count",
            ["customer_state"],
            [],
            new SubmittedDateIntent(
                "absolute", null, null, "2018-01-01", "2018-12-31",
                "none"),
            null),
        [],
        null);

    [Fact]
    public void OutboxMessage_ValidatesAndRedactsPayload()
    {
        Assert.Throws<ArgumentException>(() => new OutboxMessage(
            " ", OutboxMessageType.ReportProcessingRequested,
            "request", Now, "{}"));
        Assert.Throws<ArgumentException>(() => new OutboxMessage(
            "id", OutboxMessageType.ReportProcessingRequested,
            "request", Now.ToOffset(TimeSpan.FromHours(3)), "{}"));
        Assert.Throws<ArgumentException>(() => new OutboxMessage(
            "id", OutboxMessageType.ReportProcessingRequested,
            "request", Now, " "));

        var message = new OutboxMessage(
            "id", OutboxMessageType.ReportProcessingRequested,
            "request", Now, "{\"secret\":true}");
        Assert.DoesNotContain("secret", message.ToString(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Serializer_RoundTripsSafeEnvelopeAndRejectsUnknownSchema()
    {
        var serializer = new OutboxMessageSerializer();
        var message = new ReportProcessingRequestedMessage(
            "request", "correlation", Now);
        var json = serializer.SerializeReportProcessingRequested(message);
        var roundTrip = serializer.DeserializeReportProcessingRequested(json);

        Assert.Equal(message, roundTrip);
        Assert.Contains("\"requestId\"", json, StringComparison.Ordinal);
        foreach (var forbidden in new[]
                 {
                     "prompt", "userId", "tenantId", "allowedRegions",
                     "allowedStoreIds", "canonicalRequestJson", "sql"
                 })
            Assert.DoesNotContain(forbidden, json,
                StringComparison.OrdinalIgnoreCase);
        Assert.Throws<NotSupportedException>(() =>
            serializer.DeserializeReportProcessingRequested(
                json.Replace("\"schemaVersion\":1",
                    "\"schemaVersion\":99", StringComparison.Ordinal)));
    }

    [Fact]
    public void Serializer_RoundTripsSubmittedPlanEnvelopeV2()
    {
        var serializer = new OutboxMessageSerializer();
        var plan = new SubmittedSemanticPlanningResult(
            "accepted",
            new SubmittedSemanticIntent(
                "order_count",
                ["customer_state"],
                [],
                new SubmittedDateIntent(
                    "absolute", null, null, "2018-01-01", "2018-12-31",
                    "none"),
                null),
            [],
            null);
        var message = new ReportProcessingRequestedMessage(
            "request",
            "correlation",
            Now,
            ReportProcessingRequestedMessage.CurrentSchemaVersion,
            plan);

        var json = serializer.SerializeReportProcessingRequested(message);
        var roundTrip = serializer.DeserializeReportProcessingRequested(json);

        Assert.Equal(2, roundTrip.SchemaVersion);
        Assert.NotNull(roundTrip.SemanticPlan);
        Assert.Equal("order_count",
            roundTrip.SemanticPlan.SemanticIntent!.Metric);
    }

    [Fact]
    public void MessageId_IsDeterministicAndClarificationGenerationDiffers()
    {
        var first = OutboxMessageFactory.CreateMessageId(
            "request", "initial", Now);
        var retry = OutboxMessageFactory.CreateMessageId(
            "request", "initial", Now);
        var clarification = OutboxMessageFactory.CreateMessageId(
            "request", "clarification", Now);
        Assert.Equal(first, retry);
        Assert.NotEqual(first, clarification);
        Assert.Equal(64, first.Length);
    }

    [Fact]
    public async Task InMemoryStore_IsIdempotentClaimsRetriesAndCleansPublished()
    {
        var store = new InMemoryOutboxStore();
        var message = CreateOutbox("id-1", Now);
        await store.AppendAsync(message, CancellationToken.None);
        await store.AppendAsync(message, CancellationToken.None);
        await Assert.ThrowsAsync<OutboxMessageConflictException>(() =>
            store.AppendAsync(CreateOutbox("id-1", Now.AddSeconds(1)),
                CancellationToken.None));

        Assert.Empty(await store.ClaimPendingBatchAsync(
            1, "other", Now.AddSeconds(-1), TimeSpan.FromMinutes(1),
            CancellationToken.None));
        var claimed = Assert.Single(await store.ClaimPendingBatchAsync(
            1, "worker", Now, TimeSpan.FromMinutes(1),
            CancellationToken.None));
        Assert.Equal(1, claimed.AttemptCount);
        Assert.Empty(await store.ClaimPendingBatchAsync(
            1, "other", Now.AddSeconds(30), TimeSpan.FromMinutes(1),
            CancellationToken.None));

        await store.ReleaseForRetryAsync("id-1", "worker",
            Now.AddMinutes(2), "SERVICE_BUS_TRANSIENT",
            CancellationToken.None);
        Assert.Empty(await store.ClaimPendingBatchAsync(
            1, "other", Now.AddMinutes(1), TimeSpan.FromMinutes(1),
            CancellationToken.None));
        claimed = Assert.Single(await store.ClaimPendingBatchAsync(
            1, "other", Now.AddMinutes(2), TimeSpan.FromMinutes(1),
            CancellationToken.None));
        Assert.Equal(2, claimed.AttemptCount);
        await store.MarkPublishedAsync("id-1", "other", Now.AddMinutes(2),
            CancellationToken.None);
        Assert.Equal(1, await store.DeletePublishedBeforeAsync(
            Now.AddMinutes(3), 10, CancellationToken.None));
        Assert.Empty(store.Snapshot);
    }

    [Fact]
    public async Task ExpiredProcessingLock_IsReclaimed()
    {
        var store = new InMemoryOutboxStore();
        await store.AppendAsync(CreateOutbox("id", Now),
            CancellationToken.None);
        _ = await store.ClaimPendingBatchAsync(1, "first", Now,
            TimeSpan.FromSeconds(30), CancellationToken.None);
        var reclaimed = Assert.Single(await store.ClaimPendingBatchAsync(
            1, "second", Now.AddSeconds(31), TimeSpan.FromSeconds(30),
            CancellationToken.None));
        Assert.Equal("second", reclaimed.LockOwner);
        Assert.Equal(2, reclaimed.AttemptCount);
    }

    [Fact]
    public async Task Dispatcher_PublishesAndMarksOutboxPublished()
    {
        var store = new InMemoryOutboxStore();
        await store.AppendAsync(CreateOutbox("id", Now),
            CancellationToken.None);
        var publisher = new RecordingPublisher();
        await using var provider = CreateDispatcherServices(store, publisher);
        var dispatcher = CreateDispatcher(provider, publisher,
            maxAttempts: 3);

        Assert.Equal(1, await dispatcher.DispatchOnceAsync(
            CancellationToken.None));
        Assert.Single(publisher.Messages);
        Assert.Equal("Published", Assert.Single(store.Snapshot).Status);
    }

    [Fact]
    public async Task Dispatcher_TransientFailureSchedulesRetry()
    {
        var store = new InMemoryOutboxStore();
        await store.AppendAsync(CreateOutbox("id", Now),
            CancellationToken.None);
        var publisher = new RecordingPublisher
        {
            Exception = new InvalidOperationException("temporary")
        };
        await using var provider = CreateDispatcherServices(store, publisher);
        var dispatcher = CreateDispatcher(provider, publisher,
            maxAttempts: 3);

        await dispatcher.DispatchOnceAsync(CancellationToken.None);
        var snapshot = Assert.Single(store.Snapshot);
        Assert.Equal("Pending", snapshot.Status);
        Assert.Equal("SERVICE_BUS_TRANSIENT", snapshot.LastFailureCode);
        Assert.True(snapshot.NextAttemptAt > Now);
    }

    [Fact]
    public async Task Dispatcher_PermanentFailureDeadLettersOutbox()
    {
        var store = new InMemoryOutboxStore();
        await store.AppendAsync(CreateOutbox("id", Now),
            CancellationToken.None);
        var publisher = new RecordingPublisher
        {
            Exception = new InvalidDataException("invalid")
        };
        await using var provider = CreateDispatcherServices(store, publisher);
        var dispatcher = CreateDispatcher(provider, publisher,
            maxAttempts: 3);

        await dispatcher.DispatchOnceAsync(CancellationToken.None);
        var snapshot = Assert.Single(store.Snapshot);
        Assert.Equal("DeadLettered", snapshot.Status);
        Assert.Equal("INVALID_OUTBOX_PAYLOAD", snapshot.LastFailureCode);
        Assert.NotNull(snapshot.DeadLetteredAt);
    }

    [Fact]
    public void AzurePublisher_BuildsOnlySafeBrokerEnvelope()
    {
        var serializer = new OutboxMessageSerializer();
        var envelope = new ReportProcessingRequestedMessage(
            "request", "correlation", Now);
        var message = AzureServiceBusReportProcessingMessagePublisher
            .CreateServiceBusMessage(envelope, "message-id", serializer);

        Assert.Equal("message-id", message.MessageId);
        Assert.Equal("report-processing-requested.v1", message.Subject);
        Assert.Equal("application/json", message.ContentType);
        Assert.Equal(1, message.ApplicationProperties["schemaVersion"]);
        Assert.Equal("ReportProcessingRequested",
            message.ApplicationProperties["messageType"]);
        var body = message.Body.ToString();
        Assert.Contains("requestId", body, StringComparison.Ordinal);
        Assert.DoesNotContain("user", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tenant", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("scope", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AzureConsumerValidator_AcceptsValidAndRejectsMalformedEnvelope()
    {
        var serializer = new OutboxMessageSerializer();
        var validator = new AzureServiceBusMessageValidator(serializer);
        var envelope = new ReportProcessingRequestedMessage(
            "request", "correlation", Now);
        var properties = new Dictionary<string, object>
        {
            ["schemaVersion"] = 1,
            ["messageType"] = "ReportProcessingRequested"
        };
        var received = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString(
                serializer.SerializeReportProcessingRequested(envelope)),
            messageId: "message-id",
            subject: "report-processing-requested.v1",
            contentType: "application/json",
            properties: properties);
        Assert.Equal(envelope,
            validator.ValidateAndDeserialize(received));

        var invalidContentType = ServiceBusModelFactory
            .ServiceBusReceivedMessage(
                body: received.Body,
                messageId: "message-id",
                subject: "report-processing-requested.v1",
                contentType: "text/plain",
                properties: properties);
        Assert.Throws<InvalidDataException>(() =>
            validator.ValidateAndDeserialize(invalidContentType));

        properties["schemaVersion"] = 99;
        var unsupported = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: received.Body,
            messageId: "message-id",
            subject: "report-processing-requested.v1",
            contentType: "application/json",
            properties: properties);
        Assert.Throws<NotSupportedException>(() =>
            validator.ValidateAndDeserialize(unsupported));
    }

    [Fact]
    public void FailureClassifier_UsesServiceBusTransientFlag()
    {
        var classifier = new MessagePublishFailureClassifier();
        var transient = new ServiceBusException(
            "busy", ServiceBusFailureReason.ServiceBusy);
        var permanent = new ServiceBusException(
            "missing", ServiceBusFailureReason.MessagingEntityNotFound);
        Assert.Equal(MessagePublishFailureCategory.Transient,
            classifier.Classify(transient).Category);
        Assert.Equal(MessagePublishFailureCategory.Permanent,
            classifier.Classify(permanent).Category);
    }

    [Fact]
    public void MessagingValidation_EnforcesProductionIdentityRules()
    {
        var production = new MessagingOptionsValidator(
            new TestEnvironment("Production"));
        Assert.True(production.Validate(null, new MessagingOptions
        {
            Provider = "InMemory"
        }).Failed);
        Assert.True(production.Validate(null, ValidAzure(
            "ConnectionString")).Failed);
        Assert.False(production.Validate(null, ValidAzure(
            "ManagedIdentity")).Failed);

        var development = new MessagingOptionsValidator(
            new TestEnvironment("Development"));
        Assert.False(development.Validate(null, new MessagingOptions
        {
            Provider = "InMemory"
        }).Failed);
    }

    [Fact]
    public void MessagingRegistration_IsProviderSpecificAndClientIsSingleton()
    {
        var inMemoryServices = new ServiceCollection();
        inMemoryServices.AddLogging();
        inMemoryServices.AddCrmAnalyticsMessaging(
            Configuration(new Dictionary<string, string?>
            {
                ["Messaging:Provider"] = "InMemory"
            }), new TestEnvironment("Development"));
        Assert.Contains(inMemoryServices, descriptor =>
            descriptor.ServiceType == typeof(IReportProcessingQueue));
        Assert.DoesNotContain(inMemoryServices, descriptor =>
            descriptor.ServiceType == typeof(ServiceBusClient));
        Assert.Contains(inMemoryServices, descriptor =>
            descriptor.ServiceType == typeof(IHostedService)
            && descriptor.ImplementationType
                == typeof(ReportProcessingBackgroundService));

        var azureServices = new ServiceCollection();
        azureServices.AddLogging();
        azureServices.AddCrmAnalyticsMessaging(
            Configuration(new Dictionary<string, string?>
            {
                ["Messaging:Provider"] = "AzureServiceBus",
                ["Messaging:AzureServiceBus:AuthenticationMode"] =
                    "ManagedIdentity",
                ["Messaging:AzureServiceBus:FullyQualifiedNamespace"] =
                    "sample.servicebus.windows.net"
            }), new TestEnvironment("Production"));
        Assert.DoesNotContain(azureServices, descriptor =>
            descriptor.ServiceType == typeof(IReportProcessingQueue));
        Assert.Contains(azureServices, descriptor =>
            descriptor.ServiceType == typeof(ServiceBusClient)
            && descriptor.Lifetime == ServiceLifetime.Singleton);
        Assert.Contains(azureServices, descriptor =>
            descriptor.ServiceType == typeof(IHostedService)
            && descriptor.ImplementationType
                == typeof(AzureServiceBusReportProcessingConsumer));
    }

    [Fact]
    public async Task CreateRevisionAndClarificationAppendDistinctOutboxMessages()
    {
        var reports = new InMemoryReportRequestRepository();
        var conversations = new InMemoryConversationRepository();
        var outbox = new InMemoryOutboxStore();
        var service = new ReportRequestService(
            reports,
            new ConversationContextService(conversations, reports),
            new InMemoryApplicationTransactionRunner(),
            new InMemoryApplicationAuditWriter(),
            outbox,
            new OutboxMessageFactory(new OutboxMessageSerializer()));
        var created = await service.CreateAsync(new CreateReportRequestCommand(
            "Prompt", "conversation", null, "correlation"),
            CancellationToken.None);
        Assert.Single(outbox.Snapshot);

        var report = (await reports.GetByIdAsync(created.RequestId,
            CancellationToken.None))!;
        report.TransitionTo(ReportRequestStatus.Validating,
            report.UpdatedAt.AddTicks(1));
        report.TransitionTo(ReportRequestStatus.Processing,
            report.UpdatedAt.AddTicks(1));
        report.Complete("report", "summary",
            "https://app.powerbi.com/report", report.UpdatedAt.AddTicks(1));
        await reports.UpdateAsync(report, CancellationToken.None);
        var revision = await service.ReviseAsync(new ReviseReportRequestCommand(
            created.RequestId, "Revision", "revision-correlation"),
            CancellationToken.None);
        Assert.Equal(2, outbox.Snapshot.Count);

        var revisionReport = (await reports.GetByIdAsync(
            revision.RequestId, CancellationToken.None))!;
        var clarificationTime = revisionReport.UpdatedAt.AddTicks(1);
        revisionReport.TransitionTo(ReportRequestStatus.Validating,
            clarificationTime);
        revisionReport.RequestClarification("Which period?",
            clarificationTime.AddTicks(1));
        await reports.UpdateAsync(revisionReport, CancellationToken.None);
        await service.SubmitClarificationAsync(
            new SubmitReportClarificationCommand(
                revision.RequestId, "First quarter",
                clarificationTime.AddTicks(2)),
            CancellationToken.None);
        Assert.Equal(3, outbox.Snapshot.Count);
        Assert.Equal(3, outbox.Snapshot.Select(item =>
            item.Message.MessageId).Distinct(StringComparer.Ordinal).Count());
    }

    private static OutboxMessage CreateOutbox(
        string id, DateTimeOffset occurredAt)
    {
        var serializer = new OutboxMessageSerializer();
        var payload = serializer.SerializeReportProcessingRequested(
            new ReportProcessingRequestedMessage(
                "request", "correlation", occurredAt));
        return new OutboxMessage(id,
            OutboxMessageType.ReportProcessingRequested,
            "request", occurredAt, payload);
    }

    private static ServiceProvider CreateDispatcherServices(
        InMemoryOutboxStore store, RecordingPublisher publisher)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IOutboxStore>(store);
        services.AddSingleton<IReportProcessingMessagePublisher>(publisher);
        return services.BuildServiceProvider();
    }

    private static OutboxDispatcherBackgroundService CreateDispatcher(
        ServiceProvider provider, RecordingPublisher publisher,
        int maxAttempts)
    {
        var time = new FixedTimeProvider(Now);
        return new OutboxDispatcherBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            publisher,
            new OutboxMessageSerializer(),
            new MessagePublishFailureClassifier(),
            Options.Create(new OutboxDispatcherOptions
            {
                MaxAttempts = maxAttempts,
                BaseRetryDelaySeconds = 5,
                MaxRetryDelaySeconds = 30
            }),
            time,
            new MessagingRuntimeState(),
            NullLogger<OutboxDispatcherBackgroundService>.Instance);
    }

    private static MessagingOptions ValidAzure(string authMode) => new()
    {
        Provider = "AzureServiceBus",
        AzureServiceBus = new AzureServiceBusMessagingOptions
        {
            AuthenticationMode = authMode,
            FullyQualifiedNamespace = authMode == "ConnectionString"
                ? string.Empty
                : "sample.servicebus.windows.net",
            ConnectionString = authMode == "ConnectionString"
                ? "development-test-value"
                : string.Empty
        }
    };

    private static IConfiguration Configuration(
        IDictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private sealed class RecordingPublisher
        : IReportProcessingMessagePublisher
    {
        public List<ReportProcessingRequestedMessage> Messages { get; } = [];
        public Exception? Exception { get; init; }
        public Task PublishAsync(ReportProcessingRequestedMessage message,
            string messageId, CancellationToken cancellationToken)
        {
            if (Exception is not null) throw Exception;
            Messages.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class TestEnvironment(string environmentName)
        : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }
}
