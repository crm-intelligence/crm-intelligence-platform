using Crm.Analytics.Sql.Catalog;
using Crm.Analytics.Sql.Contracts;
using CrmAnalytics.Infrastructure.Integrations;

namespace CrmAnalytics.UnitTests;

public sealed class SemanticSourceResolutionTests
{
    private readonly CatalogSemanticSourceResolver resolver = new(
        SemanticCatalogRegistry.CreateDefault());

    [Fact]
    public void MetricDimensionFilterAndMappingIntersectionSelectsSingleSource()
    {
        var decision = resolver.Resolve(
            null,
            "payment_total",
            ["payment_type"],
            [new RequestFilter
            {
                Field = "payment_type",
                Op = FilterOperator.In,
                Values = [new FilterLiteral(FilterValueKind.Text, "credit_card")]
            }]);

        Assert.Equal(SemanticSourceDecisionKind.Resolved, decision.Kind);
        Assert.Equal(DataSource.Dwh, decision.Source);
        Assert.Equal([DataSource.Dwh], decision.CandidateSources);
        Assert.Contains(SemanticSourceEvidenceKind.QueryMappingAvailable,
            decision.EvidenceKinds);
    }

    [Fact]
    public void ExplicitIncompatibleSourceFailsClosed()
    {
        var decision = resolver.Resolve(
            DataSource.Oltp,
            "payment_total",
            ["payment_type"],
            []);

        Assert.Equal(SemanticSourceDecisionKind.Incompatible, decision.Kind);
        Assert.Null(decision.Source);
        Assert.Empty(decision.CandidateSources);
    }

    [Fact]
    public void RfmCompatibilityComesOnlyFromCatalogMetadata()
    {
        var compatible = resolver.Resolve(
            null, "avg_monetary", ["rfm_customer_city"], []);
        var incompatible = resolver.Resolve(
            null, "payment_total", ["rfm_customer_city"], []);

        Assert.Equal(DataSource.Dwh, compatible.Source);
        Assert.Equal(SemanticSourceDecisionKind.Incompatible, incompatible.Kind);
    }

    [Fact]
    public void MissingMetricDoesNotInventSourceFromDimension()
    {
        var decision = resolver.Resolve(
            null, null, ["customer_city"], []);

        Assert.Equal(SemanticSourceDecisionKind.Missing, decision.Kind);
        Assert.Null(decision.Source);
    }
}
