using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace CrmAnalytics.Infrastructure.Messaging;

public sealed class MessagingRuntimeState
{
    public volatile bool DispatcherRunning;
    public volatile bool ConsumerRunning;
    public volatile bool BrokerFailureObserved;
}

public sealed class MessagingRuntimeHealthCheck : IHealthCheck
{
    private readonly MessagingRuntimeState _state;
    private readonly MessagingOptions _options;

    public MessagingRuntimeHealthCheck(MessagingRuntimeState state,
        IOptions<MessagingOptions> options)
    {
        _state = state;
        _options = options.Value;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var azure = MessagingOptionsValidator.IsProvider(
            _options.Provider, MessagingOptions.AzureServiceBusProvider);
        var healthy = _state.DispatcherRunning
            && (!azure || _state.ConsumerRunning)
            && !_state.BrokerFailureObserved;
        return Task.FromResult(healthy
            ? HealthCheckResult.Healthy("Messaging workers are running.")
            : HealthCheckResult.Unhealthy(
                "Messaging workers are not ready."));
    }
}
