using System.Data;
using System.Data.Common;
using CrmAnalytics.Application.Abstractions.Integrations;
using CrmAnalytics.Application.SqlProduction;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrmAnalytics.Infrastructure.QueryExecution;

public sealed class SqlClientQueryExecutionClient : IQueryExecutionClient
{
    private readonly ISqlQueryConnectionFactory _connectionFactory;
    private readonly IQueryExecutionParameterBinder _parameterBinder;
    private readonly QueryResultMaterializer _materializer;
    private readonly IQueryResultReferenceFactory _referenceFactory;
    private readonly QueryExecutionOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SqlClientQueryExecutionClient> _logger;
    private readonly Func<SqlCommand, CommandBehavior, CancellationToken,
        Task<DbDataReader>> _executeReaderAsync;

    public SqlClientQueryExecutionClient(
        ISqlQueryConnectionFactory connectionFactory,
        IQueryExecutionParameterBinder parameterBinder,
        QueryResultMaterializer materializer,
        IQueryResultReferenceFactory referenceFactory,
        IOptions<QueryExecutionOptions> options,
        TimeProvider timeProvider,
        ILogger<SqlClientQueryExecutionClient> logger)
        : this(
            connectionFactory,
            parameterBinder,
            materializer,
            referenceFactory,
            options,
            timeProvider,
            logger,
            ExecuteReaderAsync)
    {
    }

    internal SqlClientQueryExecutionClient(
        ISqlQueryConnectionFactory connectionFactory,
        IQueryExecutionParameterBinder parameterBinder,
        QueryResultMaterializer materializer,
        IQueryResultReferenceFactory referenceFactory,
        IOptions<QueryExecutionOptions> options,
        TimeProvider timeProvider,
        ILogger<SqlClientQueryExecutionClient> logger,
        Func<SqlCommand, CommandBehavior, CancellationToken,
            Task<DbDataReader>> executeReaderAsync)
    {
        _connectionFactory = connectionFactory;
        _parameterBinder = parameterBinder;
        _materializer = materializer;
        _referenceFactory = referenceFactory;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
        _executeReaderAsync = executeReaderAsync;
    }

    public async Task<QueryExecutionResult> ExecuteAsync(
        SqlExecutionPlan executionPlan,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(executionPlan);
        MockQueryExecutionClient.ValidatePlan(executionPlan);
        if (executionPlan.Source == SqlDataSource.Dwh
            && !DwhQuerySurfacePolicy.Allows(executionPlan.Sql))
        {
            throw new QueryExecutionPermanentException(
                QueryExecutionErrorCodes.Failed,
                executionPlan.Source,
                TimeSpan.Zero);
        }

        if (executionPlan.Source == SqlDataSource.Oltp
            && !OltpQuerySurfacePolicy.Allows(executionPlan.Sql))
        {
            throw new QueryExecutionPermanentException(
                QueryExecutionErrorCodes.Failed,
                executionPlan.Source,
                TimeSpan.Zero);
        }

        var sourceOptions = _options.GetSource(executionPlan.Source);
        if (!sourceOptions.Enabled)
        {
            throw new QueryExecutionPermanentException(
                QueryExecutionErrorCodes.SourceDisabled,
                executionPlan.Source,
                TimeSpan.Zero);
        }

        if (executionPlan.CommandTimeoutSeconds
            > sourceOptions.CommandTimeoutCeilingSeconds)
        {
            throw new QueryExecutionPermanentException(
                QueryExecutionErrorCodes.Failed,
                executionPlan.Source,
                TimeSpan.Zero);
        }

        var startedAt = _timeProvider.GetUtcNow();
        var timestamp = _timeProvider.GetTimestamp();
        var resultReference = _referenceFactory.Create();
        for (var attempt = 0;
             attempt <= sourceOptions.MaxRetryCount;
             attempt++)
        {
            var readerStarted = false;
            var stage = "connection-open";
            try
            {
                await using var connection =
                    await _connectionFactory.CreateOpenConnectionAsync(
                        executionPlan.Source,
                        cancellationToken);
                stage = "command-create";
                await using var command = connection.CreateCommand();
                command.CommandType = CommandType.Text;
                command.CommandText = executionPlan.Sql;
                command.CommandTimeout =
                    executionPlan.CommandTimeoutSeconds;
                _parameterBinder.Bind(
                    command,
                    executionPlan.Parameters,
                    executionPlan.Source);

                stage = "execute";
                await using var reader = await _executeReaderAsync(
                    command,
                    CommandBehavior.SequentialAccess
                    | CommandBehavior.SingleResult,
                    cancellationToken);
                readerStarted = true;
                var result = await _materializer.MaterializeAsync(
                    reader,
                    executionPlan.Source,
                    sourceOptions,
                    resultReference,
                    startedAt,
                    () => _timeProvider.GetElapsedTime(timestamp),
                    cancellationToken);
                _logger.LogInformation(
                    "Query execution succeeded for source {Source} in {DurationMilliseconds} ms with {RowCount} rows; truncated: {IsTruncated}.",
                    result.Source,
                    result.Duration.TotalMilliseconds,
                    result.RowCount,
                    result.IsTruncated);
                return result;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (QueryExecutionException exception)
            {
                if (executionPlan.Source == SqlDataSource.Dwh
                    && exception.ErrorCode == QueryExecutionErrorCodes.Failed)
                {
                    LogDwhFailure(exception, stage);
                }

                throw;
            }
            catch (SqlQueryConnectionAttemptException exception)
                when (exception.Kind == SqlQueryFailureKind.Timeout)
            {
                throw new QueryExecutionTimeoutException(
                    executionPlan.Source,
                    _timeProvider.GetElapsedTime(timestamp),
                    exception);
            }
            catch (SqlQueryConnectionAttemptException exception)
                when (exception.Kind == SqlQueryFailureKind.AccessDenied)
            {
                throw new QueryExecutionPermanentException(
                    QueryExecutionErrorCodes.AccessDenied,
                    executionPlan.Source,
                    _timeProvider.GetElapsedTime(timestamp),
                    exception);
            }
            catch (SqlQueryConnectionAttemptException exception)
                when (exception.Kind == SqlQueryFailureKind.Transient
                    && attempt < sourceOptions.MaxRetryCount)
            {
                await DelayBeforeRetryAsync(
                    executionPlan.Source,
                    sourceOptions,
                    attempt,
                    cancellationToken);
            }
            catch (SqlQueryConnectionAttemptException exception)
                when (exception.Kind == SqlQueryFailureKind.Transient)
            {
                throw new QueryExecutionTransientException(
                    executionPlan.Source,
                    _timeProvider.GetElapsedTime(timestamp),
                    exception);
            }
            catch (SqlQueryConnectionAttemptException exception)
            {
                if (executionPlan.Source == SqlDataSource.Dwh)
                {
                    LogDwhFailure(exception, stage);
                }

                throw new QueryExecutionPermanentException(
                    QueryExecutionErrorCodes.Failed,
                    executionPlan.Source,
                    _timeProvider.GetElapsedTime(timestamp),
                    exception);
            }
            catch (SqlException exception) when (exception.Number == -2)
            {
                throw new QueryExecutionTimeoutException(
                    executionPlan.Source,
                    _timeProvider.GetElapsedTime(timestamp),
                    exception);
            }
            catch (SqlException exception)
                when (SqlQueryFailureClassifier.Classify(exception)
                    == SqlQueryFailureKind.AccessDenied)
            {
                throw new QueryExecutionPermanentException(
                    QueryExecutionErrorCodes.AccessDenied,
                    executionPlan.Source,
                    _timeProvider.GetElapsedTime(timestamp),
                    exception);
            }
            catch (SqlException exception)
                when (!readerStarted
                    && SqlQueryFailureClassifier.Classify(exception)
                        == SqlQueryFailureKind.Transient
                    && attempt < sourceOptions.MaxRetryCount)
            {
                await DelayBeforeRetryAsync(
                    executionPlan.Source,
                    sourceOptions,
                    attempt,
                    cancellationToken);
            }
            catch (SqlException exception)
                when (!readerStarted
                    && SqlQueryFailureClassifier.Classify(exception)
                        == SqlQueryFailureKind.Transient)
            {
                throw new QueryExecutionTransientException(
                    executionPlan.Source,
                    _timeProvider.GetElapsedTime(timestamp),
                    exception);
            }
            catch (Exception exception)
            {
                if (executionPlan.Source == SqlDataSource.Dwh)
                {
                    LogDwhFailure(exception, stage);
                }

                throw new QueryExecutionPermanentException(
                    QueryExecutionErrorCodes.Failed,
                    executionPlan.Source,
                    _timeProvider.GetElapsedTime(timestamp),
                    exception);
            }
        }

        throw new QueryExecutionTransientException(
            executionPlan.Source,
            _timeProvider.GetElapsedTime(timestamp));
    }

    private static async Task<DbDataReader> ExecuteReaderAsync(
        SqlCommand command,
        CommandBehavior behavior,
        CancellationToken cancellationToken) =>
        await command.ExecuteReaderAsync(behavior, cancellationToken);

    private void LogDwhFailure(Exception exception, string stage)
    {
        var sqlException = FindSqlException(exception);
        bool? retryable = exception is SqlQueryConnectionAttemptException attempt
            ? attempt.Kind == SqlQueryFailureKind.Transient
            : sqlException is null
                ? null
                : SqlQueryFailureClassifier.Classify(sqlException)
                    == SqlQueryFailureKind.Transient;

        if (sqlException is not null)
        {
            _logger.LogError(
                "DWH query execution failed before submission. ExceptionType: {ExceptionType}; InnerExceptionType: {InnerExceptionType}; SqlExceptionNumber: {SqlExceptionNumber}; SqlExceptionState: {SqlExceptionState}; SqlExceptionClass: {SqlExceptionClass}; Stage: {Stage}; Retryable: {Retryable}.",
                exception.GetType().FullName,
                exception.InnerException?.GetType().FullName,
                sqlException.Number,
                sqlException.State,
                sqlException.Class,
                stage,
                retryable);
            return;
        }

        if (retryable.HasValue)
        {
            _logger.LogError(
                "DWH query execution failed before submission. ExceptionType: {ExceptionType}; InnerExceptionType: {InnerExceptionType}; Stage: {Stage}; Retryable: {Retryable}.",
                exception.GetType().FullName,
                exception.InnerException?.GetType().FullName,
                stage,
                retryable.Value);
            return;
        }

        _logger.LogError(
            "DWH query execution failed before submission. ExceptionType: {ExceptionType}; InnerExceptionType: {InnerExceptionType}; Stage: {Stage}.",
            exception.GetType().FullName,
            exception.InnerException?.GetType().FullName,
            stage);
    }

    private static SqlException? FindSqlException(Exception exception)
    {
        for (Exception? current = exception;
             current is not null;
             current = current.InnerException)
        {
            if (current is SqlException sqlException)
                return sqlException;
        }

        return null;
    }

    private static TimeSpan GetRetryDelay(
        QuerySourceOptions options,
        int attempt)
    {
        var multiplier = 1L << Math.Min(attempt, 10);
        var milliseconds = Math.Min(
            options.RetryBaseDelayMilliseconds * multiplier,
            30000L);
        return TimeSpan.FromMilliseconds(milliseconds);
    }

    private async Task DelayBeforeRetryAsync(
        SqlDataSource source,
        QuerySourceOptions options,
        int attempt,
        CancellationToken cancellationToken)
    {
        var delay = GetRetryDelay(options, attempt);
        _logger.LogWarning(
            "Transient query connection failure for source {Source}; retry attempt {RetryAttempt} of {MaxRetryCount} after {DelayMilliseconds} ms.",
            source,
            attempt + 1,
            options.MaxRetryCount,
            delay.TotalMilliseconds);
        await Task.Delay(delay, _timeProvider, cancellationToken);
    }
}
