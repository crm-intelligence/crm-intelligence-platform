using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CrmAnalytics.Infrastructure.Outbox;

public sealed class OutboxDispatcherOptionsValidator
    : IValidateOptions<OutboxDispatcherOptions>
{
    private readonly IHostEnvironment _environment;
    public OutboxDispatcherOptionsValidator(IHostEnvironment environment) =>
        _environment = environment;

    public ValidateOptionsResult Validate(
        string? name, OutboxDispatcherOptions options)
    {
        if ((_environment.IsProduction() || _environment.IsStaging())
            && !options.Enabled)
            return ValidateOptionsResult.Fail(
                "The outbox dispatcher is required in Production and Staging.");
        if (options.BatchSize is < 1 or > 100
            || options.PollingIntervalSeconds is < 1 or > 60
            || options.LockDurationSeconds is < 15 or > 600
            || options.MaxAttempts is < 1 or > 100
            || options.BaseRetryDelaySeconds is < 1 or > 300
            || options.MaxRetryDelaySeconds < options.BaseRetryDelaySeconds
            || options.MaxRetryDelaySeconds > 3600
            || options.PublishedRetentionDays is < 1 or > 90)
            return ValidateOptionsResult.Fail(
                "Outbox dispatcher options are outside the allowed range.");
        return ValidateOptionsResult.Success;
    }
}
