using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CrmAnalytics.Infrastructure.Integrations;

public sealed class SqlProductionProviderOptionsValidator(
    IHostEnvironment environment)
    : IValidateOptions<SqlProductionProviderOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        SqlProductionProviderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();

        if (!IsMock(options.Provider) && !IsCrmAnalyticsSql(options.Provider))
        {
            failures.Add(
                "SqlProduction:Provider must be Mock or CrmAnalyticsSql.");
        }

        if (IsMock(options.Provider) && !IsDevelopmentOrTest(environment))
        {
            failures.Add(
                "The Mock SQL production provider is allowed only in Development or test environments.");
        }

        if (!string.Equals(options.SqlVersionName, "Sql150", StringComparison.Ordinal))
        {
            failures.Add("SqlProduction:SqlVersionName must be Sql150.");
        }

        if (options.ConfidenceThreshold is < 0 or > 1)
        {
            failures.Add(
                "SqlProduction:ConfidenceThreshold must be between 0 and 1.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    internal static bool IsMock(string? value) => string.Equals(
        value?.Trim(),
        SqlProductionProviderOptions.MockProvider,
        StringComparison.OrdinalIgnoreCase);

    internal static bool IsCrmAnalyticsSql(string? value) => string.Equals(
        value?.Trim(),
        SqlProductionProviderOptions.CrmAnalyticsSqlProvider,
        StringComparison.OrdinalIgnoreCase);

    private static bool IsDevelopmentOrTest(IHostEnvironment environment) =>
        environment.IsDevelopment()
        || environment.IsEnvironment("Test")
        || environment.IsEnvironment("Testing");
}
