using Crm.Analytics.Sql.Catalog;
using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.Contracts.V2;
using Crm.Analytics.Sql.QueryBuilder;
using Crm.Analytics.Sql.Routing;

namespace Crm.Analytics.Sql.Tests.Routing;

public sealed class QueryCapabilityAnalyzerTests
{
    private static readonly MetricCatalogDocument Catalog =
        MetricCatalogLoader.FromJson(ContractResources.ReadOlistMetricCatalog());
    private static readonly QueryCapabilityAnalyzer Analyzer = new(
        Catalog,
        DeterministicQueryCapabilities.Current);

    [Fact]
    public void ExistingBuilderSupportedShape_IsDeterministic()
    {
        var query = Query() with
        {
            Metrics = ["item_sales", "order_count"],
            Dimensions = ["customer_state", "product_category"],
            Filters =
            [
                new RequestFilter
                {
                    Field = "product_category",
                    Op = FilterOperator.Eq,
                    Values = [new FilterLiteral(FilterValueKind.Text, "electronics")]
                }
            ],
            Ordering =
            [
                new CanonicalOrdering
                {
                    TargetKind = CanonicalOrderingTargetKind.Dimension,
                    TargetKey = "customer_state",
                    Direction = SortDirection.Desc
                }
            ],
            Limit = new CanonicalLimit { Count = 10, Kind = CanonicalLimitKind.Top }
        };

        var decision = Analyzer.Analyze(query);

        Assert.Equal(QueryCapabilityOutcome.Deterministic, decision.Outcome);
        Assert.Equal(
            [QueryCapabilityReasonCode.DeterministicCapabilitiesSatisfied],
            decision.Reasons);
        Assert.Contains(QueryFeature.MultipleMetrics, decision.Features);
        Assert.Contains(QueryFeature.MultipleDimensions, decision.Features);
        Assert.Equal(ReasonCode.None, decision.FailureReasonCode);
    }

    [Fact]
    public void PeriodComparison_IsAgenticRequired()
    {
        var query = Query() with
        {
            Comparisons =
            [
                new CanonicalPeriodComparison
                {
                    Kind = PeriodComparisonKind.PreviousPeriod
                }
            ]
        };

        var decision = Analyzer.Analyze(query);

        Assert.Equal(QueryCapabilityOutcome.AgenticRequired, decision.Outcome);
        Assert.Contains(
            QueryCapabilityReasonCode.PeriodComparisonRequiresAgentic,
            decision.Reasons);
        Assert.Contains(QueryFeature.PeriodComparison, decision.Features);
    }

    [Fact]
    public void MetricOrdering_IsAgenticRequired()
    {
        var query = Query() with
        {
            Ordering =
            [
                new CanonicalOrdering
                {
                    TargetKind = CanonicalOrderingTargetKind.Metric,
                    TargetKey = "item_sales",
                    Direction = SortDirection.Desc
                }
            ],
            Limit = new CanonicalLimit { Count = 5, Kind = CanonicalLimitKind.Top }
        };

        var decision = Analyzer.Analyze(query);

        Assert.Equal(QueryCapabilityOutcome.AgenticRequired, decision.Outcome);
        Assert.Contains(
            QueryCapabilityReasonCode.MetricOrderingRequiresAgentic,
            decision.Reasons);
        Assert.Contains(QueryFeature.MetricOrdering, decision.Features);
    }

    [Fact]
    public void UnknownSemanticKey_IsUnsupported()
    {
        var decision = Analyzer.Analyze(Query() with { Metrics = ["unknown_metric"] });

        Assert.Equal(QueryCapabilityOutcome.Unsupported, decision.Outcome);
        Assert.Contains(QueryCapabilityReasonCode.UnknownMetric, decision.Reasons);
        Assert.Equal(ReasonCode.CL001, decision.FailureReasonCode);
    }

    [Fact]
    public void UnsupportedVersion_IsUnsupportedBeforeCapabilitySelection()
    {
        var decision = Analyzer.Analyze(Query() with { Version = 99 });

        Assert.Equal(QueryCapabilityOutcome.Unsupported, decision.Outcome);
        Assert.Equal(
            [QueryCapabilityReasonCode.UnsupportedCanonicalVersion],
            decision.Reasons);
        Assert.Equal(ReasonCode.GR014, decision.FailureReasonCode);
    }

    private static CanonicalQuery Query() => new()
    {
        Metrics = ["item_sales"],
        Dimensions = ["customer_state"],
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
