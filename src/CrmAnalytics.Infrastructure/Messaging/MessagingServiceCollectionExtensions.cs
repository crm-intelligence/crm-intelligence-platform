using Azure.Identity;
using Azure.Messaging.ServiceBus;
using CrmAnalytics.Application.Outbox;
using CrmAnalytics.Application.ReportProcessing;
using CrmAnalytics.Infrastructure.Outbox;
using CrmAnalytics.Infrastructure.ReportProcessing;
using CrmAnalytics.Infrastructure.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CrmAnalytics.Infrastructure.Messaging;

public static class MessagingServiceCollectionExtensions
{
    public static IServiceCollection AddCrmAnalyticsMessaging(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddOptions<MessagingOptions>()
            .Bind(configuration.GetSection(MessagingOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<MessagingOptions>,
            MessagingOptionsValidator>();
        services.AddOptions<OutboxDispatcherOptions>()
            .Bind(configuration.GetSection(OutboxDispatcherOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<OutboxDispatcherOptions>,
            OutboxDispatcherOptionsValidator>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<MessagingRuntimeState>();
        services.AddSingleton<IOutboxMessageSerializer,
            OutboxMessageSerializer>();
        services.AddSingleton<OutboxMessageFactory>();
        services.AddSingleton<AzureServiceBusMessageValidator>();
        services.AddSingleton<IMessagePublishFailureClassifier,
            MessagePublishFailureClassifier>();
        services.AddScoped<IReportProcessingMessageHandler,
            ReportProcessingMessageHandler>();
        services.AddScoped<IReportNotificationOutboxPublisher,
            ReportNotificationOutboxPublisher>();

        var provider = configuration[
            $"{MessagingOptions.SectionName}:Provider"];
        if (MessagingOptionsValidator.IsProvider(
                provider, MessagingOptions.InMemoryProvider))
        {
            services.AddSingleton<IReportProcessingQueue,
                ChannelReportProcessingQueue>();
            services.AddSingleton<IReportProcessingMessagePublisher,
                InMemoryReportProcessingMessagePublisher>();
            services.AddHostedService<ReportProcessingBackgroundService>();
        }
        else
        {
            services.AddSingleton(CreateServiceBusClient);
            services.AddSingleton(serviceProvider =>
            {
                var options = serviceProvider
                    .GetRequiredService<IOptions<MessagingOptions>>()
                    .Value.AzureServiceBus;
                return serviceProvider.GetRequiredService<ServiceBusClient>()
                    .CreateSender(options.QueueName);
            });
            services.AddSingleton(serviceProvider =>
            {
                var options = serviceProvider
                    .GetRequiredService<IOptions<MessagingOptions>>()
                    .Value.AzureServiceBus;
                return serviceProvider.GetRequiredService<ServiceBusClient>()
                    .CreateProcessor(options.QueueName,
                        new ServiceBusProcessorOptions
                        {
                            AutoCompleteMessages = false,
                            ReceiveMode = ServiceBusReceiveMode.PeekLock,
                            PrefetchCount = options.PrefetchCount,
                            MaxConcurrentCalls = options.MaxConcurrentCalls,
                            MaxAutoLockRenewalDuration = TimeSpan.FromMinutes(
                                options.MaxAutoLockRenewalMinutes)
                        });
            });
            services.AddSingleton<IReportProcessingMessagePublisher,
                AzureServiceBusReportProcessingMessagePublisher>();
            services.AddHostedService<AzureServiceBusReportProcessingConsumer>();
        }

        services.AddHostedService<OutboxDispatcherBackgroundService>();
        services.AddHostedService<OutboxCleanupBackgroundService>();
        services.AddHealthChecks().AddCheck<MessagingRuntimeHealthCheck>(
            "messaging-runtime", tags: new[] { "ready" });
        return services;
    }

    private static ServiceBusClient CreateServiceBusClient(
        IServiceProvider serviceProvider)
    {
        var options = serviceProvider
            .GetRequiredService<IOptions<MessagingOptions>>()
            .Value.AzureServiceBus;
        var clientOptions = new ServiceBusClientOptions
        {
            TransportType = string.Equals(options.TransportType,
                "AmqpWebSockets", StringComparison.OrdinalIgnoreCase)
                ? ServiceBusTransportType.AmqpWebSockets
                : ServiceBusTransportType.AmqpTcp,
            RetryOptions = new ServiceBusRetryOptions
            {
                MaxRetries = options.Retry.MaxRetries,
                Delay = TimeSpan.FromSeconds(options.Retry.DelaySeconds),
                MaxDelay = TimeSpan.FromSeconds(options.Retry.MaxDelaySeconds),
                TryTimeout = TimeSpan.FromSeconds(options.TryTimeoutSeconds),
                Mode = string.Equals(options.Retry.Mode, "Fixed",
                    StringComparison.OrdinalIgnoreCase)
                    ? ServiceBusRetryMode.Fixed
                    : ServiceBusRetryMode.Exponential
            }
        };

        if (MessagingOptionsValidator.IsProvider(options.AuthenticationMode,
                AzureServiceBusMessagingOptions.ConnectionStringMode))
            return new ServiceBusClient(options.ConnectionString, clientOptions);

        if (MessagingOptionsValidator.IsProvider(options.AuthenticationMode,
                AzureServiceBusMessagingOptions.DefaultAzureCredentialMode))
            return new ServiceBusClient(options.FullyQualifiedNamespace,
                new DefaultAzureCredential(), clientOptions);

        var managedIdentityId = string.IsNullOrWhiteSpace(
                options.ManagedIdentityClientId)
            ? ManagedIdentityId.SystemAssigned
            : ManagedIdentityId.FromUserAssignedClientId(
                options.ManagedIdentityClientId);
        var credential = new ManagedIdentityCredential(managedIdentityId);
        return new ServiceBusClient(options.FullyQualifiedNamespace,
            credential, clientOptions);
    }
}
