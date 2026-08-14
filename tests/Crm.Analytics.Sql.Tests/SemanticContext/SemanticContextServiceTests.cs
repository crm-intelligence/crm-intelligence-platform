using Crm.Analytics.Sql.Catalog;
using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.Contracts.V2;
using Crm.Analytics.Sql.Parsing;
using Crm.Analytics.Sql.SemanticContext;

namespace Crm.Analytics.Sql.Tests.SemanticContext;

public sealed class SemanticContextServiceTests
{
    private static readonly SemanticContextService Service = new(
        SemanticCatalogRegistry.CreateDefault(),
        new TSqlParserFactory());

    [Fact]
    public void MetricAndDimension_ProjectsMinimumApprovedContext()
    {
        var result = Service.GetQuerySemanticContext(DataSource.Dwh, Query());

        Assert.True(result.IsSuccessful);
        var context = result.Value!;
        var metric = Assert.Single(context.Metrics);
        Assert.Equal("order_count", metric.Key);
        Assert.Equal("COUNT(DISTINCT order_id)", metric.ApprovedExpression);
        Assert.Equal(["order_id"], metric.ApprovedExpressionColumns.ToArray());
        Assert.Equal("vw_sales", metric.LogicalSource);

        var dimension = Assert.Single(context.Dimensions);
        Assert.Equal("product_category", dimension.Key);
        Assert.Equal("product_category", dimension.ApprovedPhysicalColumn);
        Assert.True(dimension.Selectable);
        Assert.True(dimension.Groupable);
        Assert.True(dimension.Filterable);
        Assert.True(dimension.Sortable);

        var time = Assert.Single(context.TimeDimensions);
        Assert.Equal("order_purchase_timestamp", time.Key);
        Assert.True(time.IsTimeDimension);

        var source = Assert.Single(context.Sources);
        Assert.Equal("vw_sales", source.LogicalSource);
        Assert.Equal("mart.vw_sales", source.ApprovedPhysicalObject);
        Assert.Equal(
            ["order_id", "order_purchase_timestamp", "product_category"],
            source.ApprovedColumns.ToArray());
        Assert.Empty(context.Relationships);
        Assert.False(context.Capabilities.JoinExecutionEnabled);
    }

    [Fact]
    public void UnrelatedCatalogEntries_AreNotDumpedIntoQueryContext()
    {
        var context = Service.GetQuerySemanticContext(DataSource.Dwh, Query()).Value!;

        Assert.DoesNotContain(context.Metrics, metric => metric.Key == "payment_total");
        Assert.DoesNotContain(context.Dimensions, dimension => dimension.Key == "customer_city");
        Assert.DoesNotContain(context.Sources, source => source.LogicalSource == "vw_payment");
        Assert.All(context.Sources, source =>
            Assert.DoesNotContain("payment_value", source.ApprovedColumns));
    }

    [Fact]
    public void UnknownSemanticKey_FailsClosedWithStructuredFailure()
    {
        var result = Service.GetQuerySemanticContext(
            DataSource.Dwh,
            Query() with { Metrics = ["unknown_metric"] });

        Assert.False(result.IsSuccessful);
        Assert.Null(result.Value);
        Assert.Equal(SemanticContextFailureCode.UnknownMetric, result.Failure!.Code);
        Assert.Equal("unknown_metric", result.Failure.SemanticKey);
    }

    private static CanonicalQuery Query() => new()
    {
        Metrics = ["order_count"],
        Dimensions = ["product_category"],
        Filters = [],
        Time = new CanonicalTimeIntent
        {
            Range = new DateRangeSpec
            {
                Kind = DateRangeKind.Absolute,
                From = new DateOnly(2018, 1, 1),
                To = new DateOnly(2018, 12, 31)
            }
        }
    };
}
