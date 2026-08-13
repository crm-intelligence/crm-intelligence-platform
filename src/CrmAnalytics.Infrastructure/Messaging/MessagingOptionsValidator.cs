using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CrmAnalytics.Infrastructure.Messaging;

public sealed class MessagingOptionsValidator
    : IValidateOptions<MessagingOptions>
{
    private readonly IHostEnvironment _environment;
    public MessagingOptionsValidator(IHostEnvironment environment) =>
        _environment = environment;

    public ValidateOptionsResult Validate(string? name, MessagingOptions options)
    {
        var provider = options.Provider?.Trim();
        if (!IsProvider(provider, MessagingOptions.InMemoryProvider)
            && !IsProvider(provider, MessagingOptions.AzureServiceBusProvider))
            return ValidateOptionsResult.Fail("Messaging provider is invalid.");

        var productionLike = _environment.IsProduction()
            || _environment.IsStaging();
        if (productionLike
            && IsProvider(provider, MessagingOptions.InMemoryProvider))
            return ValidateOptionsResult.Fail(
                "In-memory messaging is not allowed in Production or Staging.");

        if (IsProvider(provider, MessagingOptions.InMemoryProvider))
            return ValidateOptionsResult.Success;

        var azure = options.AzureServiceBus;
        if (azure is null) return ValidateOptionsResult.Fail(
            "Azure Service Bus options are required.");
        if (string.IsNullOrWhiteSpace(azure.QueueName)
            || azure.QueueName != azure.QueueName.Trim())
            return ValidateOptionsResult.Fail("QueueName must be non-empty and trimmed.");
        if (azure.PrefetchCount is < 0 or > 1000
            || azure.MaxConcurrentCalls is < 1 or > 32
            || azure.MaxAutoLockRenewalMinutes is < 1 or > 30
            || azure.TryTimeoutSeconds is < 1 or > 120)
            return ValidateOptionsResult.Fail("Azure Service Bus numeric options are invalid.");
        if (!IsOneOf(azure.TransportType, "AmqpTcp", "AmqpWebSockets"))
            return ValidateOptionsResult.Fail("TransportType is invalid.");
        if (!IsOneOf(azure.AuthenticationMode,
                AzureServiceBusMessagingOptions.ManagedIdentityMode,
                AzureServiceBusMessagingOptions.DefaultAzureCredentialMode,
                AzureServiceBusMessagingOptions.ConnectionStringMode))
            return ValidateOptionsResult.Fail("AuthenticationMode is invalid.");

        var connectionMode = IsProvider(azure.AuthenticationMode,
            AzureServiceBusMessagingOptions.ConnectionStringMode);
        if (productionLike && connectionMode)
            return ValidateOptionsResult.Fail(
                "Connection-string authentication is not allowed in Production or Staging.");
        if (connectionMode)
        {
            if (string.IsNullOrWhiteSpace(azure.ConnectionString))
                return ValidateOptionsResult.Fail("A connection string is required.");
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(azure.ConnectionString))
                return ValidateOptionsResult.Fail(
                    "ConnectionString must be empty for identity authentication.");
            if (!IsNamespaceHost(azure.FullyQualifiedNamespace))
                return ValidateOptionsResult.Fail(
                    "FullyQualifiedNamespace must be a Service Bus host name.");
        }

        var retry = azure.Retry;
        if (retry is null
            || retry.MaxRetries is < 0 or > 10
            || retry.DelaySeconds is < 1 or > 60
            || retry.MaxDelaySeconds < retry.DelaySeconds
            || retry.MaxDelaySeconds > 300
            || !IsOneOf(retry.Mode, "Exponential", "Fixed"))
            return ValidateOptionsResult.Fail("Service Bus retry options are invalid.");

        return ValidateOptionsResult.Success;
    }

    public static bool IsProvider(string? value, string expected) =>
        string.Equals(value?.Trim(), expected, StringComparison.OrdinalIgnoreCase);

    private static bool IsOneOf(string? value, params string[] expected) =>
        expected.Any(item => IsProvider(value, item));

    private static bool IsNamespaceHost(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value != value.Trim()
            || value.Contains('/') || value.Contains(':')) return false;
        return Uri.CheckHostName(value) == UriHostNameType.Dns
            && value.EndsWith(".servicebus.windows.net",
                StringComparison.OrdinalIgnoreCase);
    }
}
