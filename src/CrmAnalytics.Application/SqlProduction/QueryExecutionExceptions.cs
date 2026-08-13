namespace CrmAnalytics.Application.SqlProduction;

public abstract class QueryExecutionException : Exception
{
    protected QueryExecutionException(
        string errorCode,
        string userMessage,
        SqlDataSource source,
        TimeSpan duration,
        Exception? innerException = null)
        : base(userMessage, innerException)
    {
        ErrorCode = errorCode;
        UserMessage = userMessage;
        DataSource = source;
        Duration = duration;
    }

    public string ErrorCode { get; }
    public string UserMessage { get; }
    public SqlDataSource DataSource { get; }
    public TimeSpan Duration { get; }
}

public sealed class QueryExecutionTransientException
    : QueryExecutionException
{
    public QueryExecutionTransientException(
        SqlDataSource source,
        TimeSpan duration,
        Exception? innerException = null)
        : base(
            QueryExecutionErrorCodes.Transient,
            QueryExecutionMessages.General,
            source,
            duration,
            innerException)
    {
    }
}

public class QueryExecutionPermanentException : QueryExecutionException
{
    public QueryExecutionPermanentException(
        string errorCode,
        SqlDataSource source,
        TimeSpan duration,
        Exception? innerException = null)
        : base(
            errorCode,
            QueryExecutionMessages.General,
            source,
            duration,
            innerException)
    {
    }
}

public sealed class QueryExecutionTimeoutException
    : QueryExecutionException
{
    public QueryExecutionTimeoutException(
        SqlDataSource source,
        TimeSpan duration,
        Exception? innerException = null)
        : base(
            QueryExecutionErrorCodes.Timeout,
            QueryExecutionMessages.Timeout,
            source,
            duration,
            innerException)
    {
    }
}

public sealed class QueryResultLimitExceededException
    : QueryExecutionException
{
    public QueryResultLimitExceededException(
        SqlDataSource source,
        TimeSpan duration = default)
        : base(
            QueryExecutionErrorCodes.ResultLimitExceeded,
            QueryExecutionMessages.ResultLimit,
            source,
            duration)
    {
    }
}

public static class QueryExecutionErrorCodes
{
    public const string Timeout = "QUERY_EXECUTION_TIMEOUT";
    public const string Transient = "QUERY_EXECUTION_TRANSIENT_ERROR";
    public const string Failed = "QUERY_EXECUTION_FAILED";
    public const string ResultLimitExceeded =
        "QUERY_RESULT_LIMIT_EXCEEDED";
    public const string ResultTypeUnsupported =
        "QUERY_RESULT_TYPE_UNSUPPORTED";
    public const string SourceDisabled = "QUERY_SOURCE_DISABLED";
    public const string AccessDenied = "QUERY_EXECUTION_ACCESS_DENIED";
}

public static class QueryExecutionMessages
{
    public const string General =
        "Veri sorgusu çalıştırılırken teknik bir sorun oluştu.";
    public const string Timeout =
        "Veri sorgusu belirlenen süre içinde tamamlanamadı.";
    public const string ResultLimit =
        "Sorgu sonucu güvenli işlem sınırlarını aştı.";
}
