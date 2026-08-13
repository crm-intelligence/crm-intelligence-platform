using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CrmAnalytics.Infrastructure.Persistence;

public sealed class PersistenceOptionsValidator
    : IValidateOptions<PersistenceOptions>
{
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;

    public PersistenceOptionsValidator(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        _configuration = configuration;
        _environment = environment;
    }

    public ValidateOptionsResult Validate(
        string? name,
        PersistenceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!IsInMemory(options.Provider)
            && !IsSqlServer(options.Provider))
        {
            return ValidateOptionsResult.Fail(
                "Persistence:Provider must be InMemory or SqlServer.");
        }

        if (IsInMemory(options.Provider)
            && !IsDevelopmentOrTest(_environment))
        {
            return ValidateOptionsResult.Fail(
                "The InMemory persistence provider is allowed only in "
                    + "Development or test environments.");
        }

        var sqlServer = options.SqlServer
            ?? new SqlServerPersistenceOptions();
        var failures = new List<string>();

        if (sqlServer.CommandTimeoutSeconds is < 1 or > 300)
        {
            failures.Add(
                "Persistence:SqlServer:CommandTimeoutSeconds must be "
                    + "between 1 and 300.");
        }

        if (sqlServer.MaxRetryCount is < 0 or > 10)
        {
            failures.Add(
                "Persistence:SqlServer:MaxRetryCount must be between "
                    + "0 and 10.");
        }

        if (sqlServer.MaxRetryDelaySeconds is < 1 or > 120)
        {
            failures.Add(
                "Persistence:SqlServer:MaxRetryDelaySeconds must be "
                    + "between 1 and 120.");
        }

        if (IsSqlServer(options.Provider)
            && string.IsNullOrWhiteSpace(
                _configuration.GetConnectionString(
                    SqlServerPersistenceOptions.ConnectionStringName)))
        {
            failures.Add(
                "ConnectionStrings:CrmAnalytics is required when the "
                    + "SqlServer persistence provider is selected.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    internal static bool IsInMemory(string? provider)
    {
        return string.Equals(
            provider?.Trim(),
            PersistenceOptions.InMemoryProvider,
            StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsSqlServer(string? provider)
    {
        return string.Equals(
            provider?.Trim(),
            PersistenceOptions.SqlServerProvider,
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDevelopmentOrTest(IHostEnvironment environment)
    {
        return environment.IsDevelopment()
            || environment.IsEnvironment("Test")
            || environment.IsEnvironment("Testing");
    }
}
