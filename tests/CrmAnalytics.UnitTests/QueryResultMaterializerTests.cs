using System.Data;
using CrmAnalytics.Application.SqlProduction;
using CrmAnalytics.Infrastructure.QueryExecution;

namespace CrmAnalytics.UnitTests;

public sealed class QueryResultMaterializerTests
{
    private readonly QueryResultMaterializer _materializer = new();

    [Fact]
    public async Task Materialize_MapsSupportedTypesAndNullByOrdinal()
    {
        var table = new DataTable();
        table.Columns.Add("text", typeof(string));
        table.Columns.Add("integer", typeof(int));
        table.Columns.Add("long", typeof(long));
        table.Columns.Add("decimal", typeof(decimal));
        table.Columns.Add("double", typeof(double));
        table.Columns.Add("flag", typeof(bool));
        table.Columns.Add("timestamp", typeof(DateTime));
        table.Columns.Add("offset", typeof(DateTimeOffset));
        table.Columns.Add("id", typeof(Guid));
        var id = Guid.NewGuid();
        table.Rows.Add(
            "safe", 7, 8L, 9.5m, 10.25d, true,
            new DateTime(2026, 7, 31, 10, 0, 0, DateTimeKind.Utc),
            new DateTimeOffset(2026, 7, 31, 10, 0, 0, TimeSpan.Zero),
            id);
        table.Rows.Add(
            DBNull.Value, 1, 2L, 3m, 4d, false,
            DateTime.UtcNow, DateTimeOffset.UtcNow, Guid.Empty);
        await using var reader = table.CreateDataReader();

        var result = await MaterializeAsync(reader);

        Assert.Equal(Enumerable.Range(0, 9),
            result.Columns.Select(column => column.Ordinal));
        Assert.Equal(
            [
                QueryResultValueKind.String,
                QueryResultValueKind.Int32,
                QueryResultValueKind.Int64,
                QueryResultValueKind.Decimal,
                QueryResultValueKind.Double,
                QueryResultValueKind.Boolean,
                QueryResultValueKind.DateTime,
                QueryResultValueKind.DateTimeOffset,
                QueryResultValueKind.Guid
            ],
            result.Columns.Select(column => column.ValueKind));
        Assert.Equal(QueryResultValueKind.Null,
            result.Rows[1].Values[0].Kind);
        Assert.Equal("safe", result.Rows[0].Values[0].Value);
        Assert.Equal(id, result.Rows[0].Values[8].Value);
        Assert.True(result.ApproximateSizeBytes > 0);
        Assert.Equal(2, result.RowCount);
    }

    [Fact]
    public async Task MaxRows_ReadsOnlyOneExtraAndMarksTruncated()
    {
        var table = CreateStringTable("one", "two", "three");
        await using var reader = table.CreateDataReader();
        var limits = CreateLimits();
        limits.MaxRows = 1;

        var result = await MaterializeAsync(reader, limits);

        Assert.Single(result.Rows);
        Assert.True(result.IsTruncated);
    }

    [Fact]
    public async Task MaxColumns_IsPermanentLimitFailure()
    {
        var table = new DataTable();
        table.Columns.Add("one", typeof(int));
        table.Columns.Add("two", typeof(int));
        await using var reader = table.CreateDataReader();
        var limits = CreateLimits();
        limits.MaxColumns = 1;

        var exception = await Assert.ThrowsAsync<
            QueryResultLimitExceededException>(
            () => MaterializeAsync(reader, limits));

        Assert.Equal(
            QueryExecutionErrorCodes.ResultLimitExceeded,
            exception.ErrorCode);
    }

    [Fact]
    public async Task OversizedCell_IsRejectedWithoutValueInException()
    {
        var table = CreateStringTable("secret-oversized-value");
        await using var reader = table.CreateDataReader();
        var limits = CreateLimits();
        limits.MaxCellCharacters = 3;

        var exception = await Assert.ThrowsAsync<
            QueryResultLimitExceededException>(
            () => MaterializeAsync(reader, limits));

        Assert.DoesNotContain("secret-oversized-value", exception.ToString());
    }

    [Fact]
    public async Task MaxBytes_IsRejected()
    {
        var table = CreateStringTable(new string('x', 2000));
        await using var reader = table.CreateDataReader();
        var limits = CreateLimits();
        limits.MaxResultBytes = 1024;

        await Assert.ThrowsAsync<QueryResultLimitExceededException>(
            () => MaterializeAsync(reader, limits));
    }

    [Fact]
    public async Task BinaryColumn_IsRejectedWithoutStringConversion()
    {
        var table = new DataTable();
        table.Columns.Add("payload", typeof(byte[]));
        table.Rows.Add(new byte[] { 1, 2, 3 });
        await using var reader = table.CreateDataReader();

        var exception = await Assert.ThrowsAsync<
            QueryExecutionPermanentException>(
            () => MaterializeAsync(reader));

        Assert.Equal(
            QueryExecutionErrorCodes.ResultTypeUnsupported,
            exception.ErrorCode);
    }

    [Fact]
    public async Task SecondResultSet_IsRejected()
    {
        var first = CreateStringTable("one");
        var second = CreateStringTable("two");
        await using var reader = new DataTableReader([first, second]);

        var exception = await Assert.ThrowsAsync<
            QueryExecutionPermanentException>(
            () => MaterializeAsync(reader));

        Assert.Equal(QueryExecutionErrorCodes.Failed, exception.ErrorCode);
    }

    [Fact]
    public async Task ResultToString_DoesNotExposeCellsOrReference()
    {
        var table = CreateStringTable("secret-cell");
        await using var reader = table.CreateDataReader();

        var result = await MaterializeAsync(reader);

        Assert.DoesNotContain("secret-cell", result.ToString());
        Assert.DoesNotContain("opaque-reference", result.ToString());
        Assert.DoesNotContain("secret-cell", result.Rows[0].ToString());
        Assert.DoesNotContain(
            "secret-cell",
            result.Rows[0].Values[0].ToString());
    }

    private Task<QueryExecutionResult> MaterializeAsync(
        DataTableReader reader,
        QuerySourceOptions? limits = null) =>
        _materializer.MaterializeAsync(
            reader,
            SqlDataSource.Dwh,
            limits ?? CreateLimits(),
            "opaque-reference",
            DateTimeOffset.UtcNow,
            () => TimeSpan.FromMilliseconds(5),
            CancellationToken.None);

    private static DataTable CreateStringTable(params string[] values)
    {
        var table = new DataTable();
        table.Columns.Add("value", typeof(string));
        foreach (var value in values)
        {
            table.Rows.Add(value);
        }

        return table;
    }

    private static QuerySourceOptions CreateLimits() => new()
    {
        Enabled = true,
        CommandTimeoutCeilingSeconds = 120,
        MaxRows = 100,
        MaxColumns = 100,
        MaxCellCharacters = 1000,
        MaxResultBytes = 1024 * 1024,
        MaxRetryCount = 0,
        RetryBaseDelayMilliseconds = 100
    };
}
