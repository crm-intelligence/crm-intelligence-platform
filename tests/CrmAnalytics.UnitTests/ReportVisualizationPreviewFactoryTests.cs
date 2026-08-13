using CrmAnalytics.Application.ReportProcessing;
using CrmAnalytics.Application.SqlProduction;
using CrmAnalytics.Contracts.Notifications;

namespace CrmAnalytics.UnitTests;

public sealed class ReportVisualizationPreviewFactoryTests
{
    [Fact]
    public void ScalarOrderCount_CreatesKpi()
    {
        var preview = ReportVisualizationPreviewFactory.Create(
            Result(
                [Column(0, "order_count", QueryResultValueKind.Int64)],
                [Row(Number(8423L))]),
            Shape("KpiCard", [], ["order_count"],
                [Metadata("order_count", "Sipariş Sayısı")]));

        Assert.NotNull(preview);
        Assert.Equal(ReportVisualizationKinds.Kpi, preview.Kind);
        Assert.Equal(8423, Assert.Single(preview.DataPoints).Value);
    }

    [Fact]
    public void ProductCategoryOrderCount_SortsAndLimitsToEight()
    {
        var rows = Enumerable.Range(1, 66)
            .Select(index => Row(Text($"category-{index}"),
                Number((long)index)))
            .ToArray();

        var preview = ReportVisualizationPreviewFactory.Create(
            Result(
                [
                    Column(0, "product_category",
                        QueryResultValueKind.String),
                    Column(1, "order_count",
                        QueryResultValueKind.Int64)
                ], rows),
            Shape("BarChart", ["product_category"], ["order_count"],
                [
                    Metadata("product_category", "Ürün Kategorisi"),
                    Metadata("order_count", "Sipariş Sayısı")
                ]));

        Assert.NotNull(preview);
        Assert.Equal(ReportVisualizationKinds.Bar, preview.Kind);
        Assert.Equal(66, preview.TotalRowCount);
        Assert.Equal(8, preview.DataPoints.Count);
        Assert.Equal(66, preview.DataPoints[0].Value);
        Assert.Equal(59, preview.DataPoints[^1].Value);
    }

    [Fact]
    public void TimeSeries_PreservesResultOrderAndCreatesLine()
    {
        var preview = ReportVisualizationPreviewFactory.Create(
            Result(
                [
                    Column(0, "order_purchase_timestamp",
                        QueryResultValueKind.Date),
                    Column(1, "order_count",
                        QueryResultValueKind.Int64)
                ],
                [
                    Row(Date(new DateOnly(2018, 1, 1)), Number(3L)),
                    Row(Date(new DateOnly(2018, 2, 1)), Number(9L))
                ]),
            Shape("LineChart", ["order_purchase_timestamp"],
                ["order_count"],
                [
                    Metadata("order_purchase_timestamp", "Sipariş Tarihi",
                        true),
                    Metadata("order_count", "Sipariş Sayısı")
                ]));

        Assert.NotNull(preview);
        Assert.Equal(ReportVisualizationKinds.Line, preview.Kind);
        Assert.Equal("2018-01-01", preview.DataPoints[0].Category);
        Assert.Equal("2018-02-01", preview.DataPoints[1].Category);
    }

    [Fact]
    public void ZeroRows_HasNoVisualization()
    {
        Assert.Null(ReportVisualizationPreviewFactory.Create(
            Result([Column(0, "order_count",
                QueryResultValueKind.Int64)], []),
            Shape("KpiCard", [], ["order_count"],
                [Metadata("order_count", "Sipariş Sayısı")])));
    }

    [Fact]
    public void NonnumericMetric_HasNoVisualization()
    {
        Assert.Null(ReportVisualizationPreviewFactory.Create(
            Result([Column(0, "order_count",
                QueryResultValueKind.String)], [Row(Text("eight"))]),
            Shape("KpiCard", [], ["order_count"],
                [Metadata("order_count", "Sipariş Sayısı")])));
    }

    private static QueryExecutionResult Result(
        IReadOnlyList<QueryResultColumn> columns,
        IReadOnlyList<QueryResultRow> rows) => new(
            "result-1", SqlDataSource.Dwh, columns, rows, false, 0,
            DateTimeOffset.UtcNow, TimeSpan.Zero);

    private static SqlResultShapeMetadata Shape(
        string visual,
        IReadOnlyCollection<string> dimensions,
        IReadOnlyCollection<string> metrics,
        IReadOnlyCollection<SqlResultColumnMetadata> columns) => new(
            visual, "test", columns, dimensions, metrics,
            dimensions.Concat(metrics).ToArray());

    private static SqlResultColumnMetadata Metadata(
        string name, string label, bool time = false) =>
        new(name, null, null, label, time);

    private static QueryResultColumn Column(
        int ordinal, string name, QueryResultValueKind kind) =>
        new(ordinal, name, kind, false);

    private static QueryResultRow Row(params QueryResultValue[] values) =>
        new(values);

    private static QueryResultValue Number(long value) =>
        QueryResultValue.Create(QueryResultValueKind.Int64, value);

    private static QueryResultValue Text(string value) =>
        QueryResultValue.Create(QueryResultValueKind.String, value);

    private static QueryResultValue Date(DateOnly value) =>
        QueryResultValue.Create(QueryResultValueKind.Date, value);
}
