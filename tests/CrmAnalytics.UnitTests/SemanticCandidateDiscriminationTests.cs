using CrmAnalytics.Infrastructure.Integrations;

namespace CrmAnalytics.UnitTests;

public sealed class SemanticCandidateDiscriminationTests
{
    [Fact]
    public void IndependentLexicalAndPhraseEvidenceReranksCloseEmbeddingCandidates()
    {
        var decision = new DeterministicSemanticCandidateDiscriminator().Decide(
            Request(
                Signal("embedding_top", .78, 0, 0, 0, 0),
                Signal("semantic_match", .72, .8, 1, .5, 3)));

        Assert.Equal("semantic_match", decision.BestCandidate);
        Assert.Equal("embedding_top", decision.RunnerUp);
        Assert.Equal(SemanticCandidateDecisionKind.Resolved, decision.Kind);
        Assert.True(decision.Margin >= .02);
        Assert.Contains(CandidateEvidenceKind.PhraseCoverage,
            decision.EvidenceKinds);
    }

    [Fact]
    public void CloseFinalScoresAbstainAsAmbiguous()
    {
        var decision = new DeterministicSemanticCandidateDiscriminator().Decide(
            Request(
                Signal("first", .75, .5, .5, .5, 2),
                Signal("second", .75, .5, .5, .5, 2)));

        Assert.Equal(SemanticCandidateDecisionKind.Ambiguous, decision.Kind);
        Assert.Equal(0, decision.Margin, 6);
    }

    [Fact]
    public void CompatibilityIntersectionExcludesHigherScoringCandidate()
    {
        var incompatible = Signal("wrong_operation", .99, 1, 1, 1, 3) with
        {
            OperationCompatible = false
        };
        var decision = new DeterministicSemanticCandidateDiscriminator().Decide(
            Request(incompatible, Signal("allowed", .75, .8, .8, .5, 3)));

        Assert.Equal("allowed", decision.BestCandidate);
        Assert.DoesNotContain(decision.RankedCandidates,
            candidate => candidate.SemanticKey == "wrong_operation"
                && candidate.CompatibilityValid);
    }

    [Fact]
    public void UnknownQueryWithoutIndependentEvidenceIsUnsupported()
    {
        var decision = new DeterministicSemanticCandidateDiscriminator().Decide(
            Request(Signal("nearest_only", .95, 0, 0, 0, 0)));

        Assert.Equal(SemanticCandidateDecisionKind.Unsupported, decision.Kind);
    }

    [Fact]
    public void MediumSimilarityWithCorroboratedCandidateEvidenceUsesPartialQwen()
    {
        var decision = new DeterministicSemanticCandidateDiscriminator().Decide(
            Request(
                Signal("nearest", .64, .25, .5, .2, 1,
                    CandidateEvidenceKind.NormalizedPhrase),
                Signal("runner_up", .62, .25, .5, .1, 1,
                    CandidateEvidenceKind.MorphologicalMatch)));

        Assert.Equal(SemanticCandidateDecisionKind.Ambiguous, decision.Kind);
    }

    [Fact]
    public void MediumSimilarityWithIsolatedAnchorIsUnsupported()
    {
        var decision = new DeterministicSemanticCandidateDiscriminator().Decide(
            Request(
                Signal("isolated", .64, .25, .5, .2, 1,
                    CandidateEvidenceKind.NormalizedPhrase),
                Signal("nearest_only", .62, 0, 0, 0, 0)));

        Assert.Equal(SemanticCandidateDecisionKind.Unsupported, decision.Kind);
    }

    [Fact]
    public void MediumSimilarityWithOnlyMorphologicalNearMatchesIsUnsupported()
    {
        var decision = new DeterministicSemanticCandidateDiscriminator().Decide(
            Request(
                Signal("first", .48, .25, .5, .2, 1,
                    CandidateEvidenceKind.MorphologicalMatch),
                Signal("second", .47, .25, .5, .1, 1,
                    CandidateEvidenceKind.MorphologicalMatch)));

        Assert.Equal(SemanticCandidateDecisionKind.Unsupported, decision.Kind);
    }

    [Fact]
    public void ContextualEmbeddingCorroboratesDistributedMorphologicalEvidence()
    {
        var decision = new DeterministicSemanticCandidateDiscriminator().Decide(
            Request(
                Signal("first", .62, .12, .5, .2, 1,
                    CandidateEvidenceKind.MorphologicalMatch),
                Signal("second", .57, .12, .3, .2, 1,
                    CandidateEvidenceKind.MorphologicalMatch)));

        Assert.Equal(SemanticCandidateDecisionKind.Ambiguous, decision.Kind);
    }

    [Fact]
    public void NearThresholdCandidateWithCatalogEvidenceUsesPartialQwen()
    {
        var decision = new DeterministicSemanticCandidateDiscriminator().Decide(
            Request(
                Signal("supported_near_threshold", .70, .30, .5, .2, 1),
                Signal("runner_up", .66, .20, .4, .1, 1)));

        Assert.Equal(SemanticCandidateDecisionKind.Ambiguous, decision.Kind);
        Assert.Equal("supported_near_threshold", decision.BestCandidate);
        Assert.True(decision.Score < .70);
        Assert.True(decision.Score >= .68);
    }

    [Fact]
    public void CloseCandidatesWithTiedIndependentEvidenceCannotBypass()
    {
        var decision = new DeterministicSemanticCandidateDiscriminator().Decide(
            Request(
                Signal("first", .82, .34, .5, .4, 2),
                Signal("second", .72, .34, .5, .2, 2)));

        Assert.Equal(SemanticCandidateDecisionKind.Ambiguous, decision.Kind);
        Assert.Equal("first", decision.BestCandidate);
        Assert.True(decision.Margin >= .02);
    }

    private static SemanticCandidateDiscriminationRequest Request(
        params SemanticCandidateSignal[] candidates) => new(
            "raw evaluation query",
            SemanticSlotKind.Metric,
            candidates,
            .70,
            .02,
            .55,
            2,
            .05,
            3);

    private static SemanticCandidateSignal Signal(
        string key,
        double embedding,
        double lexical,
        double phrase,
        double description,
        int evidence,
        params CandidateEvidenceKind[] evidenceKinds) => new(
            new SemanticCandidateDescriptor(
                key,
                "business description",
                ["conceptual alias"],
                ["aggregate"]),
            embedding,
            lexical,
            phrase,
            description,
            true,
            true,
            true,
            true,
            evidence,
            SemanticRepresentationType.FullNormalized,
            evidenceKinds.Prepend(CandidateEvidenceKind.Embedding)
                .Distinct().ToArray());
}
