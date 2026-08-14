using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.Contracts.V2;

namespace Crm.Analytics.Sql.Tests.Contracts;

public sealed class CanonicalV1ToV2AdapterTests
{
    [Fact]
    public void V2Contract_DoesNotExposePhysicalOrExecutionDetails()
    {
        var propertyNames = typeof(CanonicalQuery).GetProperties()
            .Select(property => property.Name)
            .ToArray();

        Assert.DoesNotContain("Source", propertyNames);
        Assert.DoesNotContain("Table", propertyNames);
        Assert.DoesNotContain("Column", propertyNames);
        Assert.DoesNotContain("Sql", propertyNames);
        Assert.DoesNotContain("Credentials", propertyNames);
        Assert.DoesNotContain("DataScope", propertyNames);
        Assert.DoesNotContain("ExecutionPolicy", propertyNames);
        Assert.Equal(
            ["Kind", "MetricKeys"],
            typeof(CanonicalCalculation).GetProperties()
                .Select(property => property.Name)
                .Order(StringComparer.Ordinal));
    }

    [Fact]
    public void MetricOnly_IsMappedWithoutSemanticLoss()
    {
        var v2 = CanonicalV1ToV2Adapter.Adapt(Request());

        Assert.Equal(CanonicalQuery.CurrentVersion, v2.Version);
        Assert.Equal(["item_sales"], v2.Metrics);
        Assert.Empty(v2.Dimensions);
        Assert.Empty(v2.Filters);
        Assert.Empty(v2.Comparisons);
        Assert.Empty(v2.Calculations);
        Assert.Empty(v2.Ordering);
        Assert.Null(v2.Limit);
    }

    [Fact]
    public void GroupBy_IsMappedAsDimensions()
    {
        var v2 = CanonicalV1ToV2Adapter.Adapt(Request() with
        {
            Dimensions = ["customer_state", "product_category"]
        });

        Assert.Equal(["customer_state", "product_category"], v2.Dimensions);
    }

    [Fact]
    public void Filters_PreserveFieldOperatorLiteralKindAndValue()
    {
        var filter = new RequestFilter
        {
            Field = "product_category",
            Op = FilterOperator.In,
            Values =
            [
                new FilterLiteral(FilterValueKind.Text, "electronics"),
                new FilterLiteral(FilterValueKind.Text, "furniture")
            ]
        };

        var v2 = CanonicalV1ToV2Adapter.Adapt(Request() with { Filters = [filter] });

        var mapped = Assert.Single(v2.Filters);
        Assert.Equal(filter.Field, mapped.Field);
        Assert.Equal(filter.Op, mapped.Op);
        Assert.Equal(filter.Values, mapped.Values);
    }

    [Fact]
    public void AbsoluteDateAndGrain_AreMappedWithoutSemanticLoss()
    {
        var range = new DateRangeSpec
        {
            Kind = DateRangeKind.Absolute,
            From = new DateOnly(2018, 1, 1),
            To = new DateOnly(2018, 12, 31)
        };

        var v2 = CanonicalV1ToV2Adapter.Adapt(Request() with
        {
            DateRange = range,
            Grain = TimeGrain.Month
        });

        Assert.Equal(range, v2.Time.Range);
        Assert.Equal(TimeGrain.Month, v2.Time.Grain);
    }

    [Fact]
    public void ResolvedRelativeDate_PreservesExpressionAndAbsoluteBounds()
    {
        var range = new DateRangeSpec
        {
            Kind = DateRangeKind.Relative,
            RelativeExpression = "last_quarter",
            From = new DateOnly(2026, 4, 1),
            To = new DateOnly(2026, 6, 30)
        };

        var v2 = CanonicalV1ToV2Adapter.Adapt(Request() with { DateRange = range });

        Assert.Equal(DateRangeKind.Relative, v2.Time.Range.Kind);
        Assert.Equal("last_quarter", v2.Time.Range.RelativeExpression);
        Assert.Equal(range.From, v2.Time.Range.From);
        Assert.Equal(range.To, v2.Time.Range.To);
    }

    [Theory]
    [InlineData(SortDirection.Desc, (int)CanonicalLimitKind.Top)]
    [InlineData(SortDirection.Asc, (int)CanonicalLimitKind.Bottom)]
    public void Ranking_PreservesOrderTargetDirectionAndLimit(
        SortDirection direction,
        int expectedLimitKind)
    {
        var v2 = CanonicalV1ToV2Adapter.Adapt(Request() with
        {
            Dimensions = ["customer_state"],
            OrderBy = "customer_state",
            OrderDirection = direction,
            Limit = 10
        });

        var ordering = Assert.Single(v2.Ordering);
        Assert.Equal(CanonicalOrderingTargetKind.Dimension, ordering.TargetKind);
        Assert.Equal("customer_state", ordering.TargetKey);
        Assert.Equal(direction, ordering.Direction);
        Assert.Equal(10, v2.Limit!.Count);
        Assert.Equal((CanonicalLimitKind)expectedLimitKind, v2.Limit.Kind);
    }

    private static CanonicalRequest Request() => new()
    {
        RequestId = "request-v1-v2",
        ConversationId = "conversation-v1-v2",
        Source = DataSource.Dwh,
        Intent = RequestIntent.SingleValue,
        Metrics = ["item_sales"],
        DateRange = new DateRangeSpec
        {
            Kind = DateRangeKind.Absolute,
            From = new DateOnly(2018, 1, 1),
            To = new DateOnly(2018, 12, 31)
        },
        Confidence = 1
    };
}
