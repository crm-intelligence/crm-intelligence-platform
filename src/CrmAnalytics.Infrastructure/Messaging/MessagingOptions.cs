namespace CrmAnalytics.Infrastructure.Messaging;

public sealed class MessagingOptions
{
    public const string SectionName = "Messaging";
    public const string InMemoryProvider = "InMemory";
    public const string AzureServiceBusProvider = "AzureServiceBus";
    public string Provider { get; set; } = AzureServiceBusProvider;
    public AzureServiceBusMessagingOptions AzureServiceBus { get; set; } = new();
}

public sealed class AzureServiceBusMessagingOptions
{
    public const string ManagedIdentityMode = "ManagedIdentity";
    public const string DefaultAzureCredentialMode = "DefaultAzureCredential";
    public const string ConnectionStringMode = "ConnectionString";
    public string AuthenticationMode { get; set; } = ManagedIdentityMode;
    public string FullyQualifiedNamespace { get; set; } = string.Empty;
    public string ConnectionString { get; set; } = string.Empty;
    public string QueueName { get; set; } = "crm-report-processing";
    public string ManagedIdentityClientId { get; set; } = string.Empty;
    public int PrefetchCount { get; set; }
    public int MaxConcurrentCalls { get; set; } = 1;
    public int MaxAutoLockRenewalMinutes { get; set; } = 5;
    public int TryTimeoutSeconds { get; set; } = 30;
    public string TransportType { get; set; } = "AmqpTcp";
    public ServiceBusRetryConfiguration Retry { get; set; } = new();
}

public sealed class ServiceBusRetryConfiguration
{
    public int MaxRetries { get; set; } = 5;
    public int DelaySeconds { get; set; } = 1;
    public int MaxDelaySeconds { get; set; } = 30;
    public string Mode { get; set; } = "Exponential";
}
