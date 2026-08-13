using CrmAnalytics.Application.SqlProduction;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace CrmAnalytics.Infrastructure.QueryExecution;

public sealed class SqlQueryConnectionFactory : ISqlQueryConnectionFactory
{
    private readonly QueryExecutionOptions _options;
    private readonly IConfiguration _configuration;

    public SqlQueryConnectionFactory(
        IOptions<QueryExecutionOptions> options,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _configuration = configuration;
    }

    public async ValueTask<SqlConnection> CreateOpenConnectionAsync(
        SqlDataSource source,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(source) || source == SqlDataSource.Unknown)
        {
            throw new QueryExecutionPermanentException(
                QueryExecutionErrorCodes.Failed,
                source,
                TimeSpan.Zero);
        }

        var sourceOptions = _options.GetSource(source);
        if (!sourceOptions.Enabled)
        {
            throw new QueryExecutionPermanentException(
                QueryExecutionErrorCodes.SourceDisabled,
                source,
                TimeSpan.Zero);
        }

        var configured = _configuration.GetConnectionString(
            sourceOptions.ConnectionStringName);
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new QueryExecutionPermanentException(
                QueryExecutionErrorCodes.SourceDisabled,
                source,
                TimeSpan.Zero);
        }

        SqlConnection connection;
        try
        {
            var builder = new SqlConnectionStringBuilder(configured);
            ApplyAuthentication(sourceOptions, builder);
            connection = new SqlConnection(builder.ConnectionString);
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException)
        {
            throw new QueryExecutionPermanentException(
                QueryExecutionErrorCodes.Failed,
                source,
                TimeSpan.Zero);
        }
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch (SqlException exception)
        {
            await connection.DisposeAsync();
            throw new SqlQueryConnectionAttemptException(
                SqlQueryFailureClassifier.Classify(exception),
                exception);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static void ApplyAuthentication(
        QuerySourceOptions options,
        SqlConnectionStringBuilder builder)
    {
        if (string.Equals(
                options.AuthenticationMode,
                QueryAuthenticationModes.ManagedIdentity,
                StringComparison.OrdinalIgnoreCase))
        {
            builder.Authentication =
                SqlAuthenticationMethod.ActiveDirectoryManagedIdentity;
            if (!string.IsNullOrWhiteSpace(options.ManagedIdentityClientId))
            {
                builder.UserID = options.ManagedIdentityClientId.Trim();
            }
        }
        else if (string.Equals(
                options.AuthenticationMode,
                QueryAuthenticationModes.DefaultAzureCredential,
                StringComparison.OrdinalIgnoreCase))
        {
            builder.Authentication =
                SqlAuthenticationMethod.ActiveDirectoryDefault;
        }
    }
}
