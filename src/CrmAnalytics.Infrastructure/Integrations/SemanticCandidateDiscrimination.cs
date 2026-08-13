using Crm.Analytics.Sql.Nlu;

namespace CrmAnalytics.Infrastructure.Integrations;

public enum SemanticCandidateDecisionKind
{
    Resolved,
    Ambiguous,
    Unsupported
}

/// <summary>
/// Model-safe catalog projection used by the final discriminator. It deliberately has
/// no table, view, column, expression, mapping reference, or other physical SQL data.
/// </summary>
public sealed record SemanticCandidateDescriptor(
    string SemanticKey,
    string BusinessDescription,
    IReadOnlyList<string> ConceptualAliases,
    IReadOnlyList<string> SupportedOperations);

public sealed record SemanticCandidateSignal(
    SemanticCandidateDescriptor Candidate,
    double EmbeddingSimilarity,
    double LexicalEvidence,
    double PhraseCoverage,
    double DescriptionRelevance,
    bool SourceCompatible,
    bool OperationCompatible,
    bool SlotTypeCompatible,
    bool GroupingFilterCompatible,
    int LexicalEvidenceCount,
    SemanticRepresentationType SupportingRepresentationType,
    IReadOnlyList<CandidateEvidenceKind> EvidenceKinds);

public sealed record SemanticCandidateDiscriminationRequest(
    string RawQuery,
    SemanticSlotKind SlotKind,
    IReadOnlyList<SemanticCandidateSignal> Candidates,
    double MinSimilarity,
    double AmbiguityMargin,
    double PartialQwenMinSimilarity,
    int MinLexicalEvidenceForBypass,
    double UnsupportedHighMargin,
    int CandidateCount);

public sealed record RankedSemanticCandidate(
    string SemanticKey,
    double Score,
    double EmbeddingSimilarity,
    double LexicalEvidence,
    double PhraseCoverage,
    double DescriptionRelevance,
    int LexicalEvidenceCount,
    bool CompatibilityValid,
    SemanticRepresentationType SupportingRepresentationType,
    IReadOnlyList<CandidateEvidenceKind> EvidenceKinds);

public sealed record SemanticCandidateDecision(
    SemanticCandidateDecisionKind Kind,
    string? BestCandidate,
    string? RunnerUp,
    double Score,
    double Margin,
    IReadOnlyList<CandidateEvidenceKind> EvidenceKinds,
    IReadOnlyList<RankedSemanticCandidate> RankedCandidates);

public interface ISemanticCandidateDiscriminator
{
    SemanticCandidateDecision Decide(
        SemanticCandidateDiscriminationRequest request);
}

/// <summary>
/// Generic hybrid discriminator. Embeddings establish semantic proximity; independent
/// field-aware lexical, phrase, description, operation, source and slot evidence decide
/// between the already-retrieved candidates. No business-key-specific weights exist.
/// </summary>
public sealed class DeterministicSemanticCandidateDiscriminator
    : ISemanticCandidateDiscriminator
{
    public SemanticCandidateDecision Decide(
        SemanticCandidateDiscriminationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.RawQuery);

        var ranked = request.Candidates
            .Select(candidate => Rank(candidate,
                request.MinLexicalEvidenceForBypass,
                DiscriminativeRelevance(
                    request.RawQuery, candidate, request.Candidates)))
            .OrderByDescending(candidate => candidate.CompatibilityValid)
            .ThenByDescending(candidate => candidate.Score)
            .ThenByDescending(candidate => candidate.PhraseCoverage)
            .ThenByDescending(candidate => candidate.LexicalEvidence)
            .ThenByDescending(candidate => candidate.EmbeddingSimilarity)
            .ThenBy(candidate => candidate.SemanticKey, StringComparer.Ordinal)
            .ToArray();
        var compatible = ranked.Where(candidate => candidate.CompatibilityValid)
            .ToArray();
        if (compatible.Length == 0)
        {
            return new SemanticCandidateDecision(
                SemanticCandidateDecisionKind.Unsupported,
                null,
                null,
                -1,
                0,
                [],
                ranked);
        }

        var best = compatible[0];
        var runnerUp = compatible.ElementAtOrDefault(1);
        var candidateEvidence = compatible.Max(candidate =>
            candidate.LexicalEvidenceCount);
        var distributedLexicalEvidence = compatible.Count(candidate =>
            candidate.LexicalEvidenceCount > 0) > 1;
        var hasExactCatalogTextEvidence = compatible.Any(candidate =>
            candidate.LexicalEvidenceCount > 0
            && candidate.EvidenceKinds.Contains(
                CandidateEvidenceKind.NormalizedPhrase));
        var corroboratedPoolEvidence = distributedLexicalEvidence
            && (hasExactCatalogTextEvidence
                || best.EmbeddingSimilarity >= request.PartialQwenMinSimilarity);
        var decisionScore = best.LexicalEvidenceCount == 0
            ? best.EmbeddingSimilarity : best.Score;
        var runnerUpDecisionScore = runnerUp is { LexicalEvidenceCount: 0 }
            ? runnerUp.EmbeddingSimilarity : runnerUp?.Score;
        var margin = runnerUpDecisionScore is null
            ? 1 : decisionScore - runnerUpDecisionScore.Value;
        var kind = DecideKind(
            decisionScore,
            margin,
            best.LexicalEvidenceCount,
            candidateEvidence,
            corroboratedPoolEvidence,
            request);
        return new SemanticCandidateDecision(
            kind,
            best.SemanticKey,
            runnerUp?.SemanticKey,
            decisionScore,
            margin,
            best.EvidenceKinds,
            compatible.Take(request.CandidateCount).ToArray());
    }

    private static RankedSemanticCandidate Rank(
        SemanticCandidateSignal signal,
        int minLexicalEvidenceForBypass,
        double discriminativeRelevance)
    {
        var compatibility = signal.SourceCompatible
            && signal.OperationCompatible
            && signal.SlotTypeCompatible
            && signal.GroupingFilterCompatible;
        var hybridRelevance = Math.Clamp(
            signal.EmbeddingSimilarity * .45
            + signal.LexicalEvidence * .35
            + signal.PhraseCoverage * .10
            + signal.DescriptionRelevance * .05
            + discriminativeRelevance * .05,
            0,
            1);

        // The output remains on the configured 0..1 threshold scale. Embedding is the
        // largest individual signal but cannot outvote all independent semantic evidence.
        var score = .40 + hybridRelevance * .60;
        if (minLexicalEvidenceForBypass > 0)
        {
            score *= signal.LexicalEvidenceCount switch
            {
                0 => .50,
                _ => 1
            };
        }
        var evidenceKinds = signal.EvidenceKinds
            .Concat(signal.PhraseCoverage > 0
                ? [CandidateEvidenceKind.PhraseCoverage] : [])
            .Concat(signal.DescriptionRelevance > 0
                ? [CandidateEvidenceKind.SemanticDescription] : [])
            .Concat(signal.OperationCompatible
                ? [CandidateEvidenceKind.OperationCompatibility] : [])
            .Concat(signal.GroupingFilterCompatible
                ? [CandidateEvidenceKind.GroupingFilterContext] : [])
            .Concat(signal.SourceCompatible
                ? [CandidateEvidenceKind.SourceCompatibility] : [])
            .Concat(signal.SlotTypeCompatible
                ? [CandidateEvidenceKind.SlotCompatibility] : [])
            .Distinct()
            .Order()
            .ToArray();
        return new RankedSemanticCandidate(
            signal.Candidate.SemanticKey,
            score,
            signal.EmbeddingSimilarity,
            signal.LexicalEvidence,
            signal.PhraseCoverage,
            signal.DescriptionRelevance,
            signal.LexicalEvidenceCount,
            compatibility,
            signal.SupportingRepresentationType,
            evidenceKinds);
    }

    private static SemanticCandidateDecisionKind DecideKind(
        double score,
        double margin,
        int topEvidence,
        int candidateEvidence,
        bool corroboratedPoolEvidence,
        SemanticCandidateDiscriminationRequest request)
    {
        if (score < request.PartialQwenMinSimilarity)
        {
            return topEvidence > 0
                && topEvidence >= request.MinLexicalEvidenceForBypass
                ? SemanticCandidateDecisionKind.Ambiguous
                : SemanticCandidateDecisionKind.Unsupported;
        }
        if (score < request.MinSimilarity)
        {
            // The deterministic bypass evidence threshold remains strict. The partial
            // path additionally accepts a corroborated pool: more than one retrieved
            // candidate has lexical support, plus either an exact catalog-text anchor
            // or contextual embedding support above the existing partial floor. An
            // isolated or context-free morphological near-match stays unsupported.
            var isNearDeterministicThreshold = topEvidence > 0
                && score >= request.MinSimilarity - request.AmbiguityMargin;
            var hasCandidatePoolEvidence = isNearDeterministicThreshold
                || corroboratedPoolEvidence
                || candidateEvidence >= Math.Max(1,
                    request.MinLexicalEvidenceForBypass);
            return hasCandidatePoolEvidence
                ? SemanticCandidateDecisionKind.Ambiguous
                : SemanticCandidateDecisionKind.Unsupported;
        }
        if (margin < Math.Max(request.AmbiguityMargin,
                request.UnsupportedHighMargin))
        {
            return SemanticCandidateDecisionKind.Ambiguous;
        }
        if (topEvidence >= request.MinLexicalEvidenceForBypass)
        {
            return SemanticCandidateDecisionKind.Resolved;
        }
        return topEvidence > 0
            ? SemanticCandidateDecisionKind.Ambiguous
            : SemanticCandidateDecisionKind.Unsupported;
    }

    private static double DiscriminativeRelevance(
        string rawQuery,
        SemanticCandidateSignal candidate,
        IReadOnlyList<SemanticCandidateSignal> candidates)
    {
        var queryConcepts = TurkishTextNormalizer.Tokenize(rawQuery)
            .Select(token => token.Normalized)
            .Where(token => token.Length >= 3 && !token.All(char.IsDigit))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (queryConcepts.Length == 0)
        {
            return 0;
        }
        var candidateConcepts = Concepts(candidate.Candidate);
        double matchedWeight = 0;
        double totalWeight = 0;
        foreach (var query in queryConcepts)
        {
            var documentFrequency = candidates.Count(item => Concepts(item.Candidate)
                .Any(concept => Related(query, concept)));
            var weight = documentFrequency == 0 ? 0 : 1d / documentFrequency;
            totalWeight += weight;
            if (weight > 0 && candidateConcepts.Any(concept => Related(query, concept)))
            {
                matchedWeight += weight;
            }
        }
        return totalWeight == 0 ? 0 : Math.Clamp(matchedWeight / totalWeight, 0, 1);
    }

    private static IReadOnlyList<string> Concepts(
        SemanticCandidateDescriptor descriptor) => descriptor.ConceptualAliases
        .Append(descriptor.BusinessDescription)
        .SelectMany(TurkishTextNormalizer.Tokenize)
        .Select(token => token.Normalized)
        .Where(token => token.Length >= 3 && !token.All(char.IsDigit))
        .Distinct(StringComparer.Ordinal)
        .ToArray();

    private static bool Related(string left, string right)
    {
        if (left.Equals(right, StringComparison.Ordinal))
        {
            return true;
        }
        var shorter = Math.Min(left.Length, right.Length);
        if (shorter < 4)
        {
            return false;
        }
        var common = 0;
        while (common < shorter && left[common] == right[common])
        {
            common++;
        }
        return common >= 4 && common >= Math.Ceiling(shorter * .7);
    }
}
