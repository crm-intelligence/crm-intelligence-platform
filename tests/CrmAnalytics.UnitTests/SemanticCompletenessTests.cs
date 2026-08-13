using Crm.Analytics.Sql.Catalog;
using Crm.Analytics.Sql.Contracts;
using CrmAnalytics.Infrastructure.Integrations;

namespace CrmAnalytics.UnitTests;

public sealed class SemanticCompletenessTests
{
    private static readonly DateRangeSpec ThisYear = new()
    {
        Kind = DateRangeKind.Relative,
        RelativeExpression = "this_year",
        From = new DateOnly(2026, 1, 1),
        To = new DateOnly(2026, 8, 7)
    };

    [Theory]
    [InlineData("urun grubuna gore toplam tutar")]
    [InlineData("kanal bazinda toplam tutar")]
    [InlineData("segment kiriliminda toplam tutar")]
    [InlineData("bolgelerin dagilimi")]
    public void GenericGroupingCuesRequireDimension(string prompt)
    {
        var intent = new SemanticSlotIntentDetector().Detect(prompt);

        Assert.True(intent.GroupingRequested);
        Assert.True(intent.DimensionRequested);
    }

    [Fact]
    public void PerEntityMetricWordingDoesNotInventGroupingDimension()
    {
        var intent = new SemanticSlotIntentDetector().Detect(
            "musteri basina ortalama tutar");

        Assert.False(intent.GroupingRequested);
        Assert.False(intent.DimensionRequested);
    }

    [Fact]
    public void ComparisonRequiresDimensionOrFilterContext()
    {
        var intent = new SemanticSlotIntentDetector().Detect(
            "iki grubu toplam tutarla karsilastir");
        var result = new SemanticCompletenessGate().Evaluate(
            intent, "item_sales", [], ThisYear, [], DataSource.Dwh,
            [], [], [UnresolvedConceptKind.Dimension], null);

        Assert.True(intent.ComparisonRequested);
        Assert.False(result.IsComplete);
        Assert.Contains(SemanticCompletenessSlot.Dimension, result.MissingSlots);
        Assert.Contains(SemanticCompletenessSlot.Comparison, result.MissingSlots);
    }

    [Fact]
    public void ExplicitUnresolvedRelativeDateDeniesCompleteness()
    {
        var intent = new SemanticSlotIntentDetector().Detect(
            "gelecek donemde toplam tutar");
        var result = new SemanticCompletenessGate().Evaluate(
            intent, "item_sales", [], null, [], DataSource.Dwh,
            [], [], [UnresolvedConceptKind.Date], null);

        Assert.True(intent.DateRequested);
        Assert.False(result.IsComplete);
        Assert.Contains(SemanticCompletenessSlot.Date, result.MissingSlots);
    }

    [Fact]
    public void ResolvedMetricDimensionAndDatePassCompletenessAndAssembly()
    {
        var result = Assemble(
            "bu yil urun grubu bazinda toplam satis",
            ResolvedDimension());

        Assert.Equal(SemanticCanonicalAssemblyOutcome.Assembled, result.Outcome);
        Assert.True(result.State.Completeness.IsComplete);
        Assert.Equal(["item_sales"], result.CanonicalRequest!.Metrics);
        Assert.Equal(["product_category"], result.CanonicalRequest.Dimensions);
    }

    [Fact]
    public void FilterOnlyCanonicalRecomputesStaleBreakdownIntent()
    {
        var filter = new RequestFilter
        {
            Field = "customer_state",
            Op = FilterOperator.Eq,
            Values = [new FilterLiteral(FilterValueKind.Text, "SP")]
        };
        var partial = new CanonicalRequest
        {
            RequestId = "partial-request",
            ConversationId = "conversation-1",
            Source = DataSource.Dwh,
            Intent = RequestIntent.Breakdown,
            Metrics = ["order_count"],
            Filters = [filter],
            DateRange = ThisYear,
            Confidence = .7
        };
        var semantic = new SemanticResolverResult(
            true,
            new SemanticResolutionResult(
                SemanticResolutionKind.Resolved, SemanticSlotKind.Metric,
                "order_count", .9, .4, .5, ["order_count"]),
            new SemanticResolutionResult(
                SemanticResolutionKind.Missing, SemanticSlotKind.Dimension,
                null, .8, .7, .1, ["customer_state"]),
            true,
            false,
            1,
            1,
            ResolvedDate: ThisYear,
            SlotIntent: new SemanticSlotIntentDetector().Detect(
                "SP eyalet filtresinde son 30 gun siparis sayisi", partial));

        var result = new SemanticCanonicalRequestAssembler(
                SemanticCatalogRegistry.CreateDefault())
            .Assemble(
                "request0000000000000000000000002",
                "conversation-1",
                "SP eyalet filtresinde son 30 gun siparis sayisi",
                DataSource.Dwh,
                partial,
                semantic);

        Assert.Equal(SemanticCanonicalAssemblyOutcome.Assembled, result.Outcome);
        Assert.Equal(RequestIntent.SingleValue, result.CanonicalRequest!.Intent);
        Assert.Empty(result.CanonicalRequest.Dimensions);
        Assert.Equal(filter, Assert.Single(result.CanonicalRequest.Filters));
    }

    [Fact]
    public void ResolvedMetricOnlyButGroupingRequestedDeniesBypass()
    {
        var semantic = ResolvedDimension() with
        {
            Dimension = ResolvedDimension().Dimension! with
            {
                Kind = SemanticResolutionKind.Missing,
                CandidateKey = null,
                CandidateKeys = []
            }
        };

        var result = Assemble("urun grubuna gore toplam satis bu yil", semantic);

        Assert.Equal(SemanticCanonicalAssemblyOutcome.Missing, result.Outcome);
        Assert.False(result.State.Completeness.IsComplete);
        Assert.Contains(UnresolvedConceptKind.Dimension, result.State.MissingSlots);
    }

    [Fact]
    public void MissingDimensionWithCandidatesIsEligibleOnlyAsPartialGap()
    {
        var semantic = ResolvedDimension() with
        {
            Dimension = ResolvedDimension().Dimension! with
            {
                Kind = SemanticResolutionKind.Missing,
                CandidateKey = null,
                CandidateKeys = ["product_category", "product_id"]
            }
        };

        var result = Assemble("urun grubuna gore toplam satis bu yil", semantic);

        Assert.Equal(SemanticCanonicalAssemblyOutcome.Missing, result.Outcome);
        var gap = Assert.Single(result.State.QwenResolvableSlots);
        Assert.Equal(UnresolvedConceptKind.Dimension, gap.Kind);
        Assert.Equal(["product_category", "product_id"], gap.CandidateKeys);
    }

    [Fact]
    public void StaleEvaluationBinaryAfterConfigurationChangeIsRefused()
    {
        var root = Path.Combine(Path.GetTempPath(),
            $"crm-evaluation-parity-{Guid.NewGuid():N}");
        var source = Path.Combine(root, "src", "Example");
        Directory.CreateDirectory(source);
        var configuration = Path.Combine(source, "appsettings.json");
        try
        {
            File.WriteAllText(configuration,
                "{\"SemanticEmbedding\":{\"MinSimilarity\":0.70}}");
            var buildFingerprint = EvaluationBuildParity
                .ComputeRepositoryFingerprint(root);

            File.WriteAllText(configuration,
                "{\"SemanticEmbedding\":{\"MinSimilarity\":0.60}}");
            var parity = EvaluationBuildParity.Verify(root, buildFingerprint);

            Assert.False(parity.IsMatch);
            Assert.Equal("StaleEvaluationBinary", parity.FailureKind);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static SemanticCanonicalAssemblyResult Assemble(
        string prompt,
        SemanticResolverResult semantic) =>
        new SemanticCanonicalRequestAssembler(
                SemanticCatalogRegistry.CreateDefault())
            .Assemble(
                "request0000000000000000000000001",
                "conversation-1",
                prompt,
                DataSource.Dwh,
                null,
                semantic);

    private static SemanticResolverResult ResolvedDimension() => new(
        true,
        new SemanticResolutionResult(
            SemanticResolutionKind.Resolved, SemanticSlotKind.Metric,
            "item_sales", .9, .4, .5, ["item_sales"]),
        new SemanticResolutionResult(
            SemanticResolutionKind.Resolved, SemanticSlotKind.Dimension,
            "product_category", .9, .4, .5, ["product_category"]),
        true,
        true,
        2,
        1,
        ResolvedDate: ThisYear);
}
