using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CrmAnalytics.Infrastructure.QueryExecution;

public sealed class QueryExecutionOptionsValidator
    : IValidateOptions<QueryExecutionOptions>
{
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;

    public QueryExecutionOptionsValidator(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        _configuration = configuration;
        _environment = environment;
    }

    public ValidateOptionsResult Validate(
        string? name,
        QueryExecutionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var protectedEnvironment = _environment.IsProduction()
            || _environment.IsStaging();

        if (EqualsValue(options.Provider, QueryExecutionProviders.Mock))
        {
            return protectedEnvironment
                ? ValidateOptionsResult.Fail(
                    "Mock query execution is not allowed in Production or Staging.")
                : ValidateOptionsResult.Success;
        }

        if (!EqualsValue(
                options.Provider,
                QueryExecutionProviders.SqlClient))
        {
            return ValidateOptionsResult.Fail(
                "QueryExecution.Provider must be Mock or SqlClient.");
        }

        if (!options.Dwh.Enabled && !options.Oltp.Enabled)
        {
            return ValidateOptionsResult.Fail(
                "At least one query source must be enabled.");
        }

        if (protectedEnvironment && !options.Dwh.Enabled)
        {
            return ValidateOptionsResult.Fail(
                "The DWH query source must be enabled in Production or Staging.");
        }

        var failures = new List<string>();
        ValidateSource("Dwh", options.Dwh, protectedEnvironment, failures);
        ValidateSource("Oltp", options.Oltp, protectedEnvironment, failures);
        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private void ValidateSource(
        string sourceName,
        QuerySourceOptions source,
        bool protectedEnvironment,
        ICollection<string> failures)
    {
        if (!source.Enabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(source.ConnectionStringName))
        {
            failures.Add($"{sourceName} ConnectionStringName is required.");
            return;
        }

        var connectionString = _configuration.GetConnectionString(
            source.ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            failures.Add($"{sourceName} query connection is required.");
        }

        ValidateRange(sourceName, nameof(source.CommandTimeoutCeilingSeconds),
            source.CommandTimeoutCeilingSeconds, 1, 300, failures);
        ValidateRange(sourceName, nameof(source.MaxRows), source.MaxRows,
            1, 100000, failures);
        ValidateRange(sourceName, nameof(source.MaxColumns), source.MaxColumns,
            1, 500, failures);
        ValidateRange(sourceName, nameof(source.MaxCellCharacters),
            source.MaxCellCharacters, 1, 100000, failures);
        ValidateRange(sourceName, nameof(source.MaxResultBytes),
            source.MaxResultBytes, 1024, 104857600, failures);
        ValidateRange(sourceName, nameof(source.MaxRetryCount),
            source.MaxRetryCount, 0, 5, failures);
        ValidateRange(sourceName, nameof(source.RetryBaseDelayMilliseconds),
            source.RetryBaseDelayMilliseconds, 50, 10000, failures);

        if (!IsAuthenticationMode(source.AuthenticationMode))
        {
            failures.Add($"{sourceName} AuthenticationMode is invalid.");
            return;
        }

        if (protectedEnvironment
            && EqualsValue(source.AuthenticationMode,
                QueryAuthenticationModes.ConnectionString))
        {
            failures.Add(
                $"{sourceName} ConnectionString authentication is not allowed in Production or Staging.");
        }
        else if (!_environment.IsDevelopment()
            && EqualsValue(source.AuthenticationMode,
                QueryAuthenticationModes.ConnectionString))
        {
            failures.Add(
                $"{sourceName} ConnectionString authentication is allowed only in Development.");
        }

        if (connectionString is null)
        {
            return;
        }

        SqlConnectionStringBuilder builder;
        try
        {
            builder = new SqlConnectionStringBuilder(connectionString);
        }
        catch (Exception exception) when (
            exception is ArgumentException or FormatException)
        {
            failures.Add($"{sourceName} query connection is invalid.");
            return;
        }

        if (protectedEnvironment)
        {
            ValidateProtectedConnection(sourceName, builder, failures);
        }

        ValidateAuthenticationCompatibility(
            sourceName,
            source.AuthenticationMode,
            builder,
            failures);
    }

    private static void ValidateProtectedConnection(
        string sourceName,
        SqlConnectionStringBuilder builder,
        ICollection<string> failures)
    {
        if (!builder.Encrypt)
        {
            failures.Add($"{sourceName} query connection must enable encryption.");
        }

        if (builder.TrustServerCertificate)
        {
            failures.Add($"{sourceName} query connection cannot trust the server certificate.");
        }

        if (builder.PersistSecurityInfo)
        {
            failures.Add($"{sourceName} query connection cannot persist security information.");
        }

        if (!string.IsNullOrWhiteSpace(builder.Password)
            || builder.IntegratedSecurity
            || (builder.Authentication == SqlAuthenticationMethod.NotSpecified
                && !string.IsNullOrWhiteSpace(builder.UserID)))
        {
            failures.Add($"{sourceName} query connection cannot use SQL or integrated credentials.");
        }
    }

    private static void ValidateAuthenticationCompatibility(
        string sourceName,
        string mode,
        SqlConnectionStringBuilder builder,
        ICollection<string> failures)
    {
        if (EqualsValue(mode, QueryAuthenticationModes.ManagedIdentity)
            && builder.Authentication
                != SqlAuthenticationMethod.ActiveDirectoryManagedIdentity)
        {
            failures.Add($"{sourceName} query connection authentication does not match ManagedIdentity.");
        }
        else if (EqualsValue(
                mode,
                QueryAuthenticationModes.DefaultAzureCredential)
            && builder.Authentication
                != SqlAuthenticationMethod.ActiveDirectoryDefault)
        {
            failures.Add($"{sourceName} query connection authentication does not match DefaultAzureCredential.");
        }
    }

    private static bool IsAuthenticationMode(string? value) =>
        EqualsValue(value, QueryAuthenticationModes.ManagedIdentity)
        || EqualsValue(value, QueryAuthenticationModes.DefaultAzureCredential)
        || EqualsValue(value, QueryAuthenticationModes.ConnectionString);

    private static bool EqualsValue(string? left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static void ValidateRange(
        string source,
        string property,
        long value,
        long minimum,
        long maximum,
        ICollection<string> failures)
    {
        if (value < minimum || value > maximum)
        {
            failures.Add(
                $"{source} {property} must be between {minimum} and {maximum}.");
        }
    }
}
