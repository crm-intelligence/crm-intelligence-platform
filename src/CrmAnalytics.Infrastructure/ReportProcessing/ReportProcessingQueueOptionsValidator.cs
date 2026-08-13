using CrmAnalytics.Application.ReportProcessing;
using Microsoft.Extensions.Options;

namespace CrmAnalytics.Infrastructure.ReportProcessing;

public sealed class ReportProcessingQueueOptionsValidator
    : IValidateOptions<ReportProcessingQueueOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        ReportProcessingQueueOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.Capacity is >= 1 and <= 10000
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"{nameof(options.Capacity)} must be between 1 and 10000.");
    }
}
