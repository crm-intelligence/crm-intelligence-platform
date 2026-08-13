using System.Data.Common;
using System.Text;
using CrmAnalytics.Application.SqlProduction;

namespace CrmAnalytics.Infrastructure.QueryExecution;

public sealed class QueryResultMaterializer
{
    public async Task<QueryExecutionResult> MaterializeAsync(
        DbDataReader reader,
        SqlDataSource source,
        QuerySourceOptions limits,
        string resultReference,
        DateTimeOffset executedAt,
        Func<TimeSpan> getDuration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(getDuration);

        if (reader.FieldCount > limits.MaxColumns)
        {
            throw new QueryResultLimitExceededException(
                source,
                getDuration());
        }

        var schema = TryGetSchema(reader);
        var columns = new List<QueryResultColumn>(reader.FieldCount);
        long size = 0;
        for (var ordinal = 0; ordinal < reader.FieldCount; ordinal++)
        {
            var name = reader.GetName(ordinal);
            var kind = GetValueKind(
                reader.GetFieldType(ordinal),
                GetDataTypeName(reader, ordinal),
                source,
                getDuration());
            var nullable = schema is not null
                && ordinal < schema.Count
                ? schema[ordinal].AllowDBNull ?? true
                : true;
            columns.Add(new QueryResultColumn(
                ordinal,
                name,
                kind,
                nullable));
            size = AddSize(
                size,
                Encoding.UTF8.GetByteCount(name) + 16L,
                limits,
                source,
                getDuration());
        }

        var rows = new List<QueryResultRow>(
            Math.Min(limits.MaxRows, 256));
        var isTruncated = false;
        while (await reader.ReadAsync(cancellationToken))
        {
            if (rows.Count >= limits.MaxRows)
            {
                isTruncated = true;
                break;
            }

            var values = new QueryResultValue[reader.FieldCount];
            for (var ordinal = 0; ordinal < reader.FieldCount; ordinal++)
            {
                var (value, valueSize) = ReadValue(
                    reader,
                    ordinal,
                    columns[ordinal].ValueKind,
                    limits,
                    source,
                    getDuration());
                values[ordinal] = value;
                size = AddSize(
                    size,
                    valueSize,
                    limits,
                    source,
                    getDuration());
            }

            rows.Add(new QueryResultRow(values));
        }

        if (await reader.NextResultAsync(cancellationToken))
        {
            throw new QueryExecutionPermanentException(
                QueryExecutionErrorCodes.Failed,
                source,
                getDuration());
        }

        return new QueryExecutionResult(
            resultReference,
            source,
            columns,
            rows,
            isTruncated,
            size,
            executedAt,
            getDuration());
    }

    private static IReadOnlyList<DbColumn>? TryGetSchema(
        DbDataReader reader)
    {
        try
        {
            return reader.GetColumnSchema();
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static string? GetDataTypeName(
        DbDataReader reader,
        int ordinal)
    {
        try
        {
            return reader.GetDataTypeName(ordinal);
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static QueryResultValueKind GetValueKind(
        Type type,
        string? providerTypeName,
        SqlDataSource source,
        TimeSpan duration)
    {
        if (providerTypeName is not null
            && providerTypeName.ToLowerInvariant() is
                "xml" or "binary" or "varbinary" or "image"
                or "geography" or "geometry" or "hierarchyid"
                or "sql_variant")
        {
            throw new QueryExecutionPermanentException(
                QueryExecutionErrorCodes.ResultTypeUnsupported,
                source,
                duration);
        }

        var unwrapped = Nullable.GetUnderlyingType(type) ?? type;
        if (unwrapped == typeof(string) || unwrapped == typeof(char))
            return QueryResultValueKind.String;
        if (unwrapped == typeof(int) || unwrapped == typeof(short)
            || unwrapped == typeof(byte))
            return QueryResultValueKind.Int32;
        if (unwrapped == typeof(long)) return QueryResultValueKind.Int64;
        if (unwrapped == typeof(decimal)) return QueryResultValueKind.Decimal;
        if (unwrapped == typeof(double) || unwrapped == typeof(float))
            return QueryResultValueKind.Double;
        if (unwrapped == typeof(bool)) return QueryResultValueKind.Boolean;
        if (unwrapped == typeof(DateOnly)
            || (unwrapped == typeof(DateTime)
                && string.Equals(
                    providerTypeName,
                    "date",
                    StringComparison.OrdinalIgnoreCase)))
            return QueryResultValueKind.Date;
        if (unwrapped == typeof(DateTime))
            return QueryResultValueKind.DateTime;
        if (unwrapped == typeof(DateTimeOffset))
            return QueryResultValueKind.DateTimeOffset;
        if (unwrapped == typeof(Guid)) return QueryResultValueKind.Guid;

        throw new QueryExecutionPermanentException(
            QueryExecutionErrorCodes.ResultTypeUnsupported,
            source,
            duration);
    }

    private static (QueryResultValue Value, long Size) ReadValue(
        DbDataReader reader,
        int ordinal,
        QueryResultValueKind declaredKind,
        QuerySourceOptions limits,
        SqlDataSource source,
        TimeSpan duration)
    {
        if (reader.IsDBNull(ordinal))
        {
            return (QueryResultValue.Null(), 1);
        }

        var raw = reader.GetValue(ordinal);
        return declaredKind switch
        {
            QueryResultValueKind.String => ReadString(
                raw,
                limits,
                source,
                duration),
            QueryResultValueKind.Int32 =>
                (QueryResultValue.Create(
                    QueryResultValueKind.Int32,
                    Convert.ToInt32(raw,
                        System.Globalization.CultureInfo.InvariantCulture)),
                    4),
            QueryResultValueKind.Int64 =>
                (QueryResultValue.Create(
                    QueryResultValueKind.Int64,
                    (long)raw), 8),
            QueryResultValueKind.Decimal =>
                (QueryResultValue.Create(
                    QueryResultValueKind.Decimal,
                    (decimal)raw), 16),
            QueryResultValueKind.Double =>
                (QueryResultValue.Create(
                    QueryResultValueKind.Double,
                    Convert.ToDouble(raw,
                        System.Globalization.CultureInfo.InvariantCulture)),
                    8),
            QueryResultValueKind.Boolean =>
                (QueryResultValue.Create(
                    QueryResultValueKind.Boolean,
                    (bool)raw), 1),
            QueryResultValueKind.Date =>
                (QueryResultValue.Create(
                    QueryResultValueKind.Date,
                    raw is DateOnly date
                        ? date
                        : DateOnly.FromDateTime((DateTime)raw)), 4),
            QueryResultValueKind.DateTime =>
                (QueryResultValue.Create(
                    QueryResultValueKind.DateTime,
                    (DateTime)raw), 8),
            QueryResultValueKind.DateTimeOffset =>
                (QueryResultValue.Create(
                    QueryResultValueKind.DateTimeOffset,
                    (DateTimeOffset)raw), 16),
            QueryResultValueKind.Guid =>
                (QueryResultValue.Create(
                    QueryResultValueKind.Guid,
                    (Guid)raw), 16),
            _ => throw new QueryExecutionPermanentException(
                QueryExecutionErrorCodes.ResultTypeUnsupported,
                source,
                duration)
        };
    }

    private static (QueryResultValue Value, long Size) ReadString(
        object raw,
        QuerySourceOptions limits,
        SqlDataSource source,
        TimeSpan duration)
    {
        var value = raw switch
        {
            string text => text,
            char character => character.ToString(),
            _ => throw new QueryExecutionPermanentException(
                QueryExecutionErrorCodes.ResultTypeUnsupported,
                source,
                duration)
        };
        if (value.Length > limits.MaxCellCharacters)
        {
            throw new QueryResultLimitExceededException(source, duration);
        }

        return (
            QueryResultValue.Create(QueryResultValueKind.String, value),
            Encoding.UTF8.GetByteCount(value));
    }

    private static long AddSize(
        long current,
        long added,
        QuerySourceOptions limits,
        SqlDataSource source,
        TimeSpan duration)
    {
        var total = checked(current + added);
        if (total > limits.MaxResultBytes)
        {
            throw new QueryResultLimitExceededException(source, duration);
        }

        return total;
    }
}
