using Crm.Analytics.Sql.Catalog;
using Crm.Analytics.Sql.Contracts;
using CrmAnalytics.Infrastructure.Integrations;

namespace CrmAnalytics.UnitTests;

public sealed class FilterLiteralResolutionTests
{
    private static readonly SemanticCatalogRegistry Registry =
        SemanticCatalogRegistry.CreateDefault();
    private static readonly FilterLiteralResolver Resolver = new(Registry);

    [Fact]
    public void SingleLiteralBindsToCatalogDimensionWithoutBecomingTheKey()
    {
        var result = Resolve("Istanbul sehir filtresinde siparis sayisi", "order_count");

        Assert.Equal(FilterLiteralResolutionKind.Resolved, result.ResolutionKind);
        Assert.Equal("customer_city", result.DimensionCandidate);
        var filter = Assert.IsType<RequestFilter>(result.ToRequestFilter());
        Assert.Equal(FilterOperator.Eq, filter.Op);
        Assert.Equal("Istanbul", Assert.Single(filter.Values).Raw);
        Assert.NotEqual(filter.Field, filter.Values[0].Raw);
        Assert.DoesNotContain("Istanbul", result.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("SP ve RJ eyaletlerini siparis sayisiyla kiyasla")]
    [InlineData("SP, RJ eyaletlerinde siparis sayisi")]
    public void MultipleLiteralsUseOneParameterizedInFilter(string prompt)
    {
        var result = Resolve(prompt, "order_count");

        Assert.Equal(FilterLiteralResolutionKind.Resolved, result.ResolutionKind);
        Assert.Equal("customer_state", result.DimensionCandidate);
        var filter = Assert.IsType<RequestFilter>(result.ToRequestFilter());
        Assert.Equal(FilterOperator.In, filter.Op);
        Assert.Equal(["SP", "RJ"], filter.Values.Select(value => value.Raw));
    }

    [Fact]
    public void FilterIntentWithoutDimensionBindingDeniesCompleteness()
    {
        var resolution = Resolve("SP icin siparis sayisi", "order_count");
        var intent = new SemanticSlotIntentDetector().Detect("SP icin siparis sayisi")
            with { FilterRequested = true };
        var completeness = new SemanticCompletenessGate().Evaluate(
            intent, "order_count", [], Date(), [], DataSource.Dwh,
            [], [], [UnresolvedConceptKind.Filter], null, resolution);

        Assert.Equal(FilterLiteralResolutionKind.Unbound, resolution.ResolutionKind);
        Assert.False(completeness.IsComplete);
        Assert.Contains(SemanticCompletenessSlot.Filter, completeness.MissingSlots);
    }

    [Fact]
    public void ComparisonWithUnboundLiteralsDeniesBypass()
    {
        var resolution = Resolve("SP ve RJ siparis sayisini kiyasla", "order_count");
        var intent = new SemanticSlotIntentDetector().Detect(
            "SP ve RJ siparis sayisini kiyasla") with { FilterRequested = true };
        var completeness = new SemanticCompletenessGate().Evaluate(
            intent, "order_count", [], Date(), [], DataSource.Dwh,
            [], [], [UnresolvedConceptKind.Filter], null, resolution);

        Assert.Equal(2, resolution.LiteralCount);
        Assert.False(completeness.IsComplete);
        Assert.Contains(SemanticCompletenessSlot.Comparison,
            completeness.MissingSlots);
    }

    [Fact]
    public void NonFilterableDimensionIsNotBound()
    {
        var result = Resolver.Resolve(
            "\"delivered\" operational status",
            DataSource.Oltp,
            null,
            null,
            new SemanticSlotIntent(true, false, false, false,
                true, false, false, true));

        Assert.NotEqual(FilterLiteralResolutionKind.Resolved,
            result.ResolutionKind);
        Assert.Null(result.ToRequestFilter());
    }

    [Fact]
    public void MetricIncompatibleFilterIsRejected()
    {
        var result = Resolve(
            "Elektronik urun kategorisinde ortalama musteri harcamasi",
            "avg_monetary");

        Assert.Equal(FilterLiteralResolutionKind.Unsupported,
            result.ResolutionKind);
        Assert.Null(result.ToRequestFilter());
    }

    [Fact]
    public void SentenceInitialTitleCaseWordIsNotAFilterLiteral()
    {
        var result = Resolver.Resolve(
            "Bir alicinin ortalama order frequency degerini hesapla",
            DataSource.Dwh, "avg_frequency", null,
            new SemanticSlotIntent(true, false, false, false,
                false, false, false, false));

        Assert.Equal(0, result.LiteralCount);
        Assert.Null(result.ToRequestFilter());
    }

    [Fact]
    public void InflectedCatalogDimensionIsNotAFilterLiteral()
    {
        var result = Resolver.Resolve(
            "Kategoriler kiriliminda urunlerden gelen para akisi",
            DataSource.Dwh, "item_sales", null,
            new SemanticSlotIntent(true, true, true, false,
                false, false, false, false));

        Assert.Equal(0, result.LiteralCount);
        Assert.Null(result.ToRequestFilter());
    }

    [Theory]
    [InlineData("Alici sehirlerinin ortalama parasal degeri")]
    [InlineData("Dun sehir kiriliminda urun fiyat toplami")]
    public void GroupingQualifierOrDateIsNotAFilterLiteral(string prompt)
    {
        var result = Resolver.Resolve(prompt, DataSource.Dwh, "item_sales", null,
            new SemanticSlotIntent(true, true, true, true,
                false, false, false, false));

        Assert.Equal(0, result.LiteralCount);
        Assert.Null(result.ToRequestFilter());
    }

    [Fact]
    public void ProperNameFollowedByForIsAnUnboundFilterNotABypassCandidate()
    {
        var result = Resolver.Resolve("Istanbul icin bu ay toplam tutar",
            DataSource.Dwh, "customer_paid_total",
            new SemanticResolutionResult(SemanticResolutionKind.Missing,
                SemanticSlotKind.Dimension, null, .6, .5, .1,
                ["customer_city", "customer_state"]),
            new SemanticSlotIntent(true, false, false, true,
                false, false, false, false));

        Assert.Equal(FilterLiteralResolutionKind.Unbound, result.ResolutionKind);
        Assert.Equal(1, result.LiteralCount);
        Assert.Null(result.ToRequestFilter());
    }

    [Fact]
    public void LowercaseConjunctionValuesBindWithoutBusinessValueMapping()
    {
        var result = Resolve(
            "teslim ve iptal siparis durumlarini siparis adediyle kiyasla",
            "order_count");

        Assert.Equal(FilterLiteralResolutionKind.Resolved, result.ResolutionKind);
        Assert.Equal("order_status", result.DimensionCandidate);
        Assert.Equal(2, result.LiteralCount);
    }

    [Theory]
    [InlineData("customer_count")]
    [InlineData("avg_monetary")]
    [InlineData("avg_frequency")]
    public void SupportedRfmMetricsResolveAuthoritativeSource(string metric)
    {
        var semantic = Semantic(metric, "rfm_customer_city");
        var result = new SemanticCanonicalRequestAssembler(Registry).Assemble(
            "request0000000000000000000000001", "conversation-1",
            "musteri sehri bazinda rfm analizi", null, null, semantic);

        Assert.Equal(SemanticCanonicalAssemblyOutcome.Assembled, result.Outcome);
        Assert.Equal(DataSource.Dwh, result.CanonicalRequest!.Source);
    }

    [Fact]
    public void RfmMetricWithIncompatibleDimensionIsRejected()
    {
        var result = new SemanticCanonicalRequestAssembler(Registry).Assemble(
            "request0000000000000000000000001", "conversation-1",
            "urun kategorisine gore ortalama musteri harcamasi", null, null,
            Semantic("avg_monetary", "product_category"));

        Assert.NotEqual(SemanticCanonicalAssemblyOutcome.Assembled, result.Outcome);
        Assert.Contains(UnresolvedConceptKind.Source,
            result.State.UnsupportedSlots);
    }

    private static FilterLiteralResolution Resolve(string prompt, string metric) =>
        Resolver.Resolve(prompt, DataSource.Dwh, metric, null,
            new SemanticSlotIntent(true, false, false, false,
                true, prompt.Contains("kiyas", StringComparison.Ordinal),
                false, false));

    private static SemanticResolverResult Semantic(string metric, string dimension) =>
        new(true,
            new SemanticResolutionResult(SemanticResolutionKind.Resolved,
                SemanticSlotKind.Metric, metric, .9, .5, .4, [metric]),
            new SemanticResolutionResult(SemanticResolutionKind.Resolved,
                SemanticSlotKind.Dimension, dimension, .9, .5, .4, [dimension]),
            true, true, 0, 0,
            ResolvedDate: DateRangeSpec.NotApplicable,
            SlotIntent: new SemanticSlotIntent(true, true, true, false,
                false, false, false, false));

    private static DateRangeSpec Date() => new()
    {
        Kind = DateRangeKind.Relative,
        RelativeExpression = "this_month",
        From = new DateOnly(2026, 8, 1),
        To = new DateOnly(2026, 8, 7)
    };
}
