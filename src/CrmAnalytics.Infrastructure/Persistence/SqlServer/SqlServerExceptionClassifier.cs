using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CrmAnalytics.Infrastructure.Persistence.SqlServer;

internal static class SqlServerExceptionClassifier
{
    public static bool IsDuplicateKey(DbUpdateException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        for (Exception? current = exception;
             current is not null;
             current = current.InnerException)
        {
            if (current is SqlException
                {
                    Number: 2601 or 2627
                })
            {
                return true;
            }

            // SQLite is used only by relational repository tests.
            if (current is DbException dbException
                && string.Equals(
                    current.GetType().FullName,
                    "Microsoft.Data.Sqlite.SqliteException",
                    StringComparison.Ordinal)
                && (dbException.ErrorCode == 19
                    || current.GetType()
                        .GetProperty("SqliteErrorCode")?
                        .GetValue(current) is 19))
            {
                return true;
            }
        }

        return false;
    }
}
