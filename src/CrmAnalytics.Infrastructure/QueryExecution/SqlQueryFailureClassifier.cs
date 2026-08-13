using Microsoft.Data.SqlClient;

namespace CrmAnalytics.Infrastructure.QueryExecution;

public enum SqlQueryFailureKind
{
    Permanent,
    Transient,
    Timeout,
    AccessDenied
}

public sealed class SqlQueryConnectionAttemptException : Exception
{
    public SqlQueryConnectionAttemptException(
        SqlQueryFailureKind kind,
        Exception? innerException = null)
        : base("The query source connection attempt failed.", innerException)
    {
        Kind = kind;
    }

    public SqlQueryFailureKind Kind { get; }
}

internal static class SqlQueryFailureClassifier
{
    private static readonly HashSet<int> TransientErrorNumbers =
    [
        20, 64, 233, 10053, 10054, 10060, 10928, 10929,
        40197, 40501, 40613, 49918, 49919, 49920
    ];

    private static readonly HashSet<int> AccessDeniedErrorNumbers =
        [229, 916, 18456];

    public static SqlQueryFailureKind Classify(SqlException exception)
    {
        if (exception.Number == -2)
            return SqlQueryFailureKind.Timeout;
        if (AccessDeniedErrorNumbers.Contains(exception.Number))
            return SqlQueryFailureKind.AccessDenied;
        if (exception.Number == 0 && exception.Class == 20)
            return SqlQueryFailureKind.Transient;
        return TransientErrorNumbers.Contains(exception.Number)
            ? SqlQueryFailureKind.Transient
            : SqlQueryFailureKind.Permanent;
    }
}
