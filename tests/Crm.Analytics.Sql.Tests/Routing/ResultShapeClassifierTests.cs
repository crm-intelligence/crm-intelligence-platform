using Crm.Analytics.Sql.Catalog;
using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.Routing;

namespace Crm.Analytics.Sql.Tests.Routing;

public class ResultShapeClassifierTests
{
    private static readonly MetricCatalogDocument Catalog =
        MetricCatalogLoader.FromJson(ContractResources.ReadOlistMetricCatalog());

    [Fact]
    public void Kirilimsiz_tek_olcum_KPI_karti_onerir()
    {
        var shape = Classify(["item_sales"], []);

        Assert.Equal(VisualType.KpiCard, shape.SuggestedVisual);
        Assert.Equal(0, shape.DimensionCount);
    }

    [Fact]
    public void Kategorik_kirilim_bar_onerir()
    {
        var shape = Classify(["item_sales"], ["customer_state"]);

        Assert.Equal(VisualType.BarChart, shape.SuggestedVisual);
        Assert.False(shape.HasTimeDimension);
    }

    [Fact]
    public void Zaman_kirilimi_cizgi_onerir()
    {
        var shape = Classify(["item_sales"], ["order_purchase_timestamp"], TimeGrain.Month);

        Assert.Equal(VisualType.LineChart, shape.SuggestedVisual);
        Assert.True(shape.HasTimeDimension);
        Assert.Contains("Month", shape.Rationale, StringComparison.Ordinal);
    }

    [Fact]
    public void Iki_kirilim_matrix_onerir()
    {
        var shape = Classify(["item_sales"], ["customer_state", "product_category"]);

        Assert.Equal(VisualType.Matrix, shape.SuggestedVisual);
        Assert.Equal(2, shape.DimensionCount);
    }

    [Fact]
    public void Olcumsuz_talep_tablo_onerir()
    {
        var shape = Classify([], ["customer_state"]);

        Assert.Equal(VisualType.Table, shape.SuggestedVisual);
    }

    [Fact]
    public void Coklu_olcum_tek_kategorik_kirilim_bar_onerir()
    {
        var shape = Classify(["item_sales", "order_count"], ["customer_state"]);

        Assert.Equal(VisualType.BarChart, shape.SuggestedVisual);
        Assert.Equal(2, shape.MetricCount);
    }

    [Fact]
    public void Her_oneri_gerekce_tasir()
    {
        // Gerekce olmadan "neden bar degil cizgi" tartismasi her rapor icin bastan yapilirdi.
        var shapes = new[]
        {
            Classify(["item_sales"], []),
            Classify(["item_sales"], ["customer_state"]),
            Classify(["item_sales"], ["order_purchase_timestamp"], TimeGrain.Month),
            Classify(["item_sales"], ["customer_state", "product_category"]),
            Classify([], ["customer_state"])
        };

        Assert.All(shapes, shape => Assert.False(string.IsNullOrWhiteSpace(shape.Rationale)));
    }

    private static ResultShape Classify(
        IReadOnlyList<string> metrics,
        IReadOnlyList<string> dimensions,
        TimeGrain grain = TimeGrain.None) =>
        ResultShapeClassifier.Classify(
            new CanonicalRequest
            {
                RequestId = "req_1",
                ConversationId = "conv_1",
                Intent = RequestIntent.Breakdown,
                Metrics = metrics,
                Dimensions = dimensions,
                Grain = grain,
                DateRange = new DateRangeSpec
                {
                    Kind = DateRangeKind.Relative,
                    RelativeExpression = "last_quarter"
                }
            },
            Catalog);
}
