namespace CrmAnalytics.Application.SqlProduction;

public sealed class QueryExecutionResult
{
    public QueryExecutionResult(
        string resultReference,
        SqlDataSource source,
        IReadOnlyList<QueryResultColumn> columns,
        IReadOnlyList<QueryResultRow> rows,
        bool isTruncated,
        long approximateSizeBytes,
        DateTimeOffset executedAt,
        TimeSpan duration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resultReference);
        if (!Enum.IsDefined(source) || source == SqlDataSource.Unknown)
        {
            throw new ArgumentOutOfRangeException(nameof(source));
        }

        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(rows);
        if (approximateSizeBytes < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(approximateSizeBytes));
        }

        if (duration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }

        ResultReference = resultReference.Trim();
        Source = source;
        Columns = Array.AsReadOnly(columns.ToArray());
        Rows = Array.AsReadOnly(rows.ToArray());
        RowCount = Rows.Count;
        IsTruncated = isTruncated;
        ApproximateSizeBytes = approximateSizeBytes;
        ExecutedAt = executedAt.ToUniversalTime();
        Duration = duration;
    }

    public string ResultReference { get; }
    public SqlDataSource Source { get; }
    public IReadOnlyList<QueryResultColumn> Columns { get; }
    public IReadOnlyList<QueryResultRow> Rows { get; }
    public int RowCount { get; }
    public bool IsTruncated { get; }
    public long ApproximateSizeBytes { get; }
    public DateTimeOffset ExecutedAt { get; }
    public TimeSpan Duration { get; }

    public override string ToString() =>
        $"QueryExecutionResult {{ Source = {Source}, RowCount = {RowCount}, "
        + $"IsTruncated = {IsTruncated} }}";
}

public sealed record QueryResultColumn(
    int Ordinal,
    string Name,
    QueryResultValueKind ValueKind,
    bool IsNullable);

public sealed class QueryResultRow
{
    public QueryResultRow(IReadOnlyList<QueryResultValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        Values = Array.AsReadOnly(values.ToArray());
    }

    public IReadOnlyList<QueryResultValue> Values { get; }

    public override string ToString() =>
        $"QueryResultRow {{ ValueCount = {Values.Count}, Data = redacted }}";
}

public sealed class QueryResultValue
{
    private QueryResultValue(QueryResultValueKind kind, object? value)
    {
        Kind = kind;
        Value = value;
    }

    public QueryResultValueKind Kind { get; }
    public object? Value { get; }

    public static QueryResultValue Null() =>
        new(QueryResultValueKind.Null, null);

    public static QueryResultValue Create(
        QueryResultValueKind kind,
        object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (kind == QueryResultValueKind.Null)
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        return new QueryResultValue(kind, value);
    }

    public override string ToString() =>
        $"QueryResultValue {{ Kind = {Kind}, Data = redacted }}";
}

public enum QueryResultValueKind
{
    Null = 0,
    String = 1,
    Int32 = 2,
    Int64 = 3,
    Decimal = 4,
    Double = 5,
    Boolean = 6,
    Date = 7,
    DateTime = 8,
    DateTimeOffset = 9,
    Guid = 10
}
