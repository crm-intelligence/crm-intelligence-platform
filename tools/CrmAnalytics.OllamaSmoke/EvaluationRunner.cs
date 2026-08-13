using System.Diagnostics;
using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.Service;
using CrmAnalytics.Infrastructure.Integrations;

namespace CrmAnalytics.OllamaSmoke;

internal sealed record QualityCase(
    string Id,
    string Prompt,
    string? Metric,
    string? Dimension,
    DateRangeKind? DateKind,
    string ExpectedOutcome,
    bool Unsupported = false,
    bool Ambiguous = false,
    bool MeasureFilter = false,
    string? FilterDimension = null,
    int? FilterLiteralCount = null,
    DataSource RequestSource = DataSource.Dwh,
    DataSource? ExpectedSource = DataSource.Dwh);

internal sealed record EvaluationSummary(
    string Suite,
    int Count,
    int MetricCorrect,
    int DimensionCorrect,
    int DateCorrect,
    int FullSemanticSuccess,
    int QwenInvoked,
    int DeterministicBypass,
    int CorrectDeterministicBypass,
    int Clarification,
    int UnsupportedCount,
    int UnsupportedCorrect,
    int QwenInvokedForUnsupported,
    int UnsafeSubstitution,
    int QueryBuilderCalledForUnsupported,
    int SqlGeneratedForUnsupported,
    long AverageTotalMilliseconds)
{
    public bool HasSafeBypass => DeterministicBypass == CorrectDeterministicBypass;
    public bool HasSafeUnsupported => UnsupportedCorrect
        == UnsupportedCount && QwenInvokedForUnsupported == 0
        && QueryBuilderCalledForUnsupported == 0
        && SqlGeneratedForUnsupported == 0 && UnsafeSubstitution == 0;
}

internal sealed class EvaluationRunner(
    ISemanticEmbeddingResolver resolver,
    IOllamaStructuredPlanningClient ollama,
    ISqlProductionService production,
    ISemanticCanonicalRequestAssembler assembler,
    DateOnly today)
{
    public async Task<EvaluationSummary> RunAsync(
        string suite,
        IReadOnlyList<QualityCase> cases,
        CancellationToken cancellationToken)
    {
        var metricCorrectCount = 0;
        var dimensionCorrectCount = 0;
        var dateCorrectCount = 0;
        var fullSuccessCount = 0;
        var qwenCount = 0;
        var bypassCount = 0;
        var correctBypassCount = 0;
        var clarificationCount = 0;
        var unsupportedCorrectCount = 0;
        var unsupportedQwenCount = 0;
        var unsafeSubstitutionCount = 0;
        var unsupportedQueryBuilderCount = 0;
        var unsupportedSqlCount = 0;
        var filterMeasuredCount = 0;
        var filterCorrectCount = 0;
        var sourceMeasuredCount = 0;
        var sourceCorrectCount = 0;
        var candidateRecallMeasuredCount = 0;
        var candidateRecallCount = 0;
        var ambiguousCount = 0;
        var ambiguousSafeCount = 0;
        var ambiguousUnsafeAcceptedCount = 0;
        var supportedCount = 0;
        var supportedSuccessCount = 0;
        var totalDurations = new List<long>();
        var embeddingOnlyDurations = new List<long>();
        var qwenAssistedDurations = new List<long>();
        long totalMilliseconds = 0;
        long deterministicParserMilliseconds = 0;
        long intentMilliseconds = 0;
        long preprocessingMilliseconds = 0;
        long embeddingMilliseconds = 0;
        long similarityMilliseconds = 0;
        long completenessMilliseconds = 0;
        long assemblyMilliseconds = 0;
        long qwenMilliseconds = 0;
        long validationMilliseconds = 0;

        foreach (var item in cases)
        {
            var totalTimer = Stopwatch.StartNew();
            var request = new SqlProductionRequest
            {
                RequestId = $"evaluation-{suite.ToLowerInvariant()}-{item.Id}",
                ConversationId = $"evaluation-{suite.ToLowerInvariant()}-conversation-{item.Id}",
                Prompt = item.Prompt,
                Today = today,
                Source = item.RequestSource,
                Scope = UserDataScope.Unrestricted
            };
            var parserTimer = Stopwatch.StartNew();
            var deterministic = production.Produce(request);
            parserTimer.Stop();
            deterministicParserMilliseconds += parserTimer.ElapsedMilliseconds;

            var planningPath = PlanningPath.DeterministicParser;
            var qwenInvoked = false;
            var queryBuilderCalled = deterministic.Decision == GuardrailDecision.Accepted;
            var sqlGenerated = queryBuilderCalled
                && !string.IsNullOrWhiteSpace(deterministic.Sql);
            var backendDiagnostic = string.Empty;
            var semantic = new SemanticResolverResult(
                false, null, null, false, false, 0, 0, "DeterministicBypass");
            SemanticPlanningState? planningState = null;
            OllamaStructuredPlanningResult planned;

            if (deterministic.Decision == GuardrailDecision.Accepted)
            {
                planned = AcceptedPlan(deterministic.CanonicalRequest!,
                    "DeterministicAccepted");
            }
            else
            {
                semantic = await resolver.ResolveAsync(
                    item.Prompt, item.RequestSource, deterministic.CanonicalRequest,
                    today, cancellationToken);
                intentMilliseconds += semantic.IntentDetectionMilliseconds;
                preprocessingMilliseconds += semantic.PreprocessingMilliseconds;
                embeddingMilliseconds += semantic.QueryEmbeddingMilliseconds;
                similarityMilliseconds += semantic.SimilaritySearchMilliseconds;
                if (!semantic.Available)
                {
                    planningPath = PlanningPath.Clarification;
                    planned = NonAcceptedPlan(
                        PlanningOutcome.NeedsClarification,
                        UnresolvedConceptKind.Metric);
                }
                else
                {
                    var assembly = assembler.Assemble(
                        request.RequestId, request.ConversationId, item.Prompt,
                        item.RequestSource, deterministic.CanonicalRequest, semantic);
                    planningState = assembly.State;
                    assemblyMilliseconds += assembly.DurationMilliseconds;
                    completenessMilliseconds +=
                        assembly.State.Completeness.DurationMilliseconds;
                    if (assembly.Outcome == SemanticCanonicalAssemblyOutcome.Assembled)
                    {
                        planningPath = PlanningPath.EmbeddingDeterministic;
                        planned = AcceptedPlan(assembly.CanonicalRequest!,
                            "EmbeddingDeterministic");
                    }
                    else if (assembly.Outcome
                        == SemanticCanonicalAssemblyOutcome.Unsupported)
                    {
                        planningPath = PlanningPath.Unsupported;
                        planned = NonAcceptedPlan(PlanningOutcome.Unsupported,
                            assembly.State.UnsupportedSlots.FirstOrDefault(
                                UnresolvedConceptKind.Metric));
                    }
                    else if (CanResolveWithPartialQwen(assembly.State))
                    {
                        qwenInvoked = true;
                        planningPath = PlanningPath.EmbeddingPlusQwen;
                        var constraints = GapConstraints(assembly.State);
                        var gap = await ollama.ResolveGapsAsync(
                            new SemanticGapPlanningRequest(
                                request.RequestId, request.ConversationId, today,
                                item.Prompt, assembly.State, constraints),
                            cancellationToken);
                        qwenMilliseconds += gap.DurationMilliseconds;
                        if (gap.Selection is
                                { Outcome: SemanticGapOutcome.Resolved } selection
                            && GapSelectionAllowed(
                                selection, assembly.State, constraints))
                        {
                            var completed = assembler.Assemble(
                                request.RequestId, request.ConversationId,
                                item.Prompt, item.RequestSource,
                                deterministic.CanonicalRequest,
                                CompleteSemantic(semantic, assembly.State,
                                    selection));
                            planningState = completed.State;
                            assemblyMilliseconds += completed.DurationMilliseconds;
                            completenessMilliseconds +=
                                completed.State.Completeness.DurationMilliseconds;
                            planned = completed.Outcome
                                == SemanticCanonicalAssemblyOutcome.Assembled
                                    ? AcceptedPlan(completed.CanonicalRequest!,
                                        "PartialQwen", gap.DurationMilliseconds,
                                        gap.PromptTokenCount,
                                        gap.CompletionTokenCount)
                                    : NonAcceptedPlan(
                                        PlanningOutcome.NeedsClarification,
                                        FirstUnresolved(completed.State),
                                        gap.DurationMilliseconds);
                        }
                        else
                        {
                            planned = NonAcceptedPlan(
                                PlanningOutcome.NeedsClarification,
                                gap.Selection?.Clarification?.Kind
                                    ?? FirstUnresolved(assembly.State),
                                gap.DurationMilliseconds);
                        }
                    }
                    else
                    {
                        planningPath = PlanningPath.Clarification;
                        planned = NonAcceptedPlan(
                            PlanningOutcome.NeedsClarification,
                            FirstUnresolved(assembly.State));
                    }
                }
            }

            qwenCount += qwenInvoked ? 1 : 0;
            var canonical = planned.CanonicalRequest;
            var outcome = deterministic.Decision == GuardrailDecision.Accepted
                ? deterministic.Decision.ToString()
                : planned.SemanticOutcome?.ToString() ?? "Invalid";
            var backendReason = deterministic.Decision == GuardrailDecision.Accepted
                ? deterministic.ReasonCode.ToString()
                : planned.ReasonCode;
            if (deterministic.Decision != GuardrailDecision.Accepted
                && planned.SemanticOutcome == PlanningOutcome.Accepted
                && canonical is not null)
            {
                var validationTimer = Stopwatch.StartNew();
                queryBuilderCalled = true;
                var backend = production.ProduceCanonical(request, canonical);
                validationTimer.Stop();
                validationMilliseconds += validationTimer.ElapsedMilliseconds;
                outcome = backend.Decision.ToString();
                backendReason = backend.ReasonCode.ToString();
                sqlGenerated = !string.IsNullOrWhiteSpace(backend.Sql);
                backendDiagnostic = string.Join(',', backend.Checks
                    .Where(check => check.Outcome == CheckOutcome.Failed)
                    .Select(check => $"{check.Name}:{check.Detail}"));
            }

            var metricCorrect = item.Metric is null
                ? canonical?.Metrics.Count is null or 0
                : canonical?.Metrics.Contains(item.Metric,
                    StringComparer.Ordinal) == true;
            var dimensionCorrect = item.Dimension is null
                ? canonical?.Dimensions.Count is null or 0
                : canonical?.Dimensions.Contains(item.Dimension,
                    StringComparer.Ordinal) == true
                    || canonical?.Filters.Any(filter =>
                        filter.Field == item.Dimension) == true;
            var dateCorrect = item.Unsupported || item.DateKind is null
                || canonical?.DateRange.Kind == item.DateKind;
            var filterCorrect = !item.MeasureFilter
                || (item.FilterDimension is null
                    ? canonical?.Filters.Count is null or 0
                    : canonical?.Filters.Any(filter =>
                        filter.Field == item.FilterDimension
                        && (item.FilterLiteralCount is null
                            || filter.Values.Count == item.FilterLiteralCount)) == true);
            var sourceCorrect = item.ExpectedSource is null
                || item.Unsupported || item.Ambiguous
                || canonical?.Source == item.ExpectedSource;
            var metricRecall = item.Metric is null
                || semantic.Metric?.RetrievalCandidateKeys?.Contains(
                    item.Metric, StringComparer.Ordinal) == true
                || semantic.Metric?.CandidateKey == item.Metric;
            var dimensionRecall = item.Dimension is null
                || semantic.Dimension?.RetrievalCandidateKeys?.Contains(
                    item.Dimension, StringComparer.Ordinal) == true
                || semantic.Dimension?.CandidateKey == item.Dimension;
            var candidateRecall = metricRecall && dimensionRecall;
            var unsafeSubstitution = item.Unsupported
                && planned.SemanticOutcome == PlanningOutcome.Accepted
                && canonical?.Metrics.Count > 0;
            var correct = metricCorrect && dimensionCorrect && dateCorrect
                && filterCorrect && sourceCorrect
                && !unsafeSubstitution && outcome == item.ExpectedOutcome;
            metricCorrectCount += metricCorrect ? 1 : 0;
            dimensionCorrectCount += dimensionCorrect ? 1 : 0;
            dateCorrectCount += dateCorrect ? 1 : 0;
            if (item.MeasureFilter)
            {
                filterMeasuredCount++;
                filterCorrectCount += filterCorrect ? 1 : 0;
            }
            if (item.ExpectedSource is not null && !item.Unsupported && !item.Ambiguous)
            {
                sourceMeasuredCount++;
                sourceCorrectCount += sourceCorrect ? 1 : 0;
            }
            if (semantic.Available && !item.Unsupported && !item.Ambiguous
                && (item.Metric is not null || item.Dimension is not null))
            {
                candidateRecallMeasuredCount++;
                candidateRecallCount += candidateRecall ? 1 : 0;
            }
            if (item.Ambiguous)
            {
                ambiguousCount++;
                var safe = outcome is "NeedsClarification" or "Unsupported"
                    && !queryBuilderCalled && !sqlGenerated;
                ambiguousSafeCount += safe ? 1 : 0;
                ambiguousUnsafeAcceptedCount += !safe && outcome == "Accepted" ? 1 : 0;
            }
            else if (!item.Unsupported)
            {
                supportedCount++;
                supportedSuccessCount += correct ? 1 : 0;
            }
            fullSuccessCount += correct ? 1 : 0;
            clarificationCount += outcome == "NeedsClarification" ? 1 : 0;
            if (planningPath == PlanningPath.EmbeddingDeterministic)
            {
                bypassCount++;
                correctBypassCount += correct ? 1 : 0;
            }
            if (item.Unsupported)
            {
                var unsupportedSafe = outcome is "Unsupported" or "NeedsClarification"
                    && !qwenInvoked && !queryBuilderCalled && !sqlGenerated
                    && !unsafeSubstitution;
                unsupportedCorrectCount += unsupportedSafe ? 1 : 0;
                unsupportedQwenCount += qwenInvoked ? 1 : 0;
                unsafeSubstitutionCount += unsafeSubstitution ? 1 : 0;
                unsupportedQueryBuilderCount += queryBuilderCalled ? 1 : 0;
                unsupportedSqlCount += sqlGenerated ? 1 : 0;
            }

            totalTimer.Stop();
            totalMilliseconds += totalTimer.ElapsedMilliseconds;
            totalDurations.Add(totalTimer.ElapsedMilliseconds);
            (qwenInvoked ? qwenAssistedDurations : embeddingOnlyDurations)
                .Add(totalTimer.ElapsedMilliseconds);
            Console.WriteLine(string.Join(" | ",
                $"Suite={suite}",
                $"Case={item.Id}",
                $"Outcome={outcome}",
                $"MetricCorrect={metricCorrect}",
                $"DimensionCorrect={dimensionCorrect}",
                $"DateCorrect={dateCorrect}",
                $"FilterCorrect={filterCorrect}",
                $"SourceCorrect={sourceCorrect}",
                $"SourceDecision={planningState?.SourceDecision?.Kind.ToString() ?? "None"}",
                $"AmbiguousSlots={string.Join(',', planningState?.AmbiguousSlots.Select(slot => slot.Kind) ?? [])}",
                $"UnsupportedSlots={string.Join(',', planningState?.UnsupportedSlots ?? [])}",
                $"MissingSlots={string.Join(',', planningState?.MissingSlots ?? [])}",
                $"CandidateRecall={candidateRecall}",
                $"PlanningPath={planningPath}",
                $"Completeness={canonical is not null}",
                $"MetricDecision={semantic.Metric?.Kind.ToString() ?? "Unavailable"}",
                $"MetricTop={semantic.Metric?.CandidateKeys.FirstOrDefault() ?? "None"}",
                $"MetricRunnerUp={semantic.Metric?.Decision?.RunnerUp ?? "None"}",
                $"MetricSimilarity={semantic.Metric?.Similarity.ToString("0.000") ?? "None"}",
                $"MetricMargin={semantic.Metric?.Margin.ToString("0.000") ?? "None"}",
                $"MetricLexicalEvidence={semantic.Metric?.LexicalEvidenceCount.ToString() ?? "None"}",
                $"MetricEvidence={FormatEvidence(semantic.Metric)}",
                $"DimensionDecision={semantic.Dimension?.Kind.ToString() ?? "Unavailable"}",
                $"DimensionTop={semantic.Dimension?.CandidateKeys.FirstOrDefault() ?? "None"}",
                $"DimensionRunnerUp={semantic.Dimension?.Decision?.RunnerUp ?? "None"}",
                $"DimensionSimilarity={semantic.Dimension?.Similarity.ToString("0.000") ?? "None"}",
                $"DimensionMargin={semantic.Dimension?.Margin.ToString("0.000") ?? "None"}",
                $"DimensionLexicalEvidence={semantic.Dimension?.LexicalEvidenceCount.ToString() ?? "None"}",
                $"DimensionEvidence={FormatEvidence(semantic.Dimension)}",
                $"FilterLiteralDecision={semantic.FilterLiteral?.ResolutionKind.ToString() ?? "None"}",
                $"FilterLiteralCount={semantic.FilterLiteral?.LiteralCount.ToString() ?? "0"}",
                $"FilterDimensionCandidate={semantic.FilterLiteral?.DimensionCandidate ?? "None"}",
                $"CanonicalMetric={string.Join(',', canonical?.Metrics ?? [])}",
                $"CanonicalDimension={string.Join(',', canonical?.Dimensions ?? [])}",
                $"CanonicalFilters={string.Join(',', canonical?.Filters.Select(filter => filter.Field) ?? [])}",
                $"CanonicalSource={canonical?.Source?.ToString() ?? "None"}",
                $"CanonicalIntent={canonical?.Intent.ToString() ?? "None"}",
                $"CanonicalUnresolved={string.Join(',', canonical?.UnresolvedTerms ?? [])}",
                $"QwenInvoked={qwenInvoked}",
                $"QueryBuilderCalled={queryBuilderCalled}",
                $"SqlGenerated={sqlGenerated}",
                $"Reason={backendReason}",
                $"BackendDiagnostic={backendDiagnostic}",
                $"DurationMilliseconds={totalTimer.ElapsedMilliseconds}"));
        }

        var count = cases.Count;
        var summary = new EvaluationSummary(
            suite, count, metricCorrectCount, dimensionCorrectCount,
            dateCorrectCount, fullSuccessCount, qwenCount, bypassCount,
            correctBypassCount, clarificationCount,
            cases.Count(item => item.Unsupported), unsupportedCorrectCount,
            unsupportedQwenCount,
            unsafeSubstitutionCount, unsupportedQueryBuilderCount,
            unsupportedSqlCount,
            count == 0 ? 0 : totalMilliseconds / count);
        Console.WriteLine(string.Join(" | ",
            $"Suite={suite}Summary",
            $"Count={count}",
            $"MetricAccuracy={metricCorrectCount}/{count}",
            $"DimensionAccuracy={dimensionCorrectCount}/{count}",
            $"DateAccuracy={dateCorrectCount}/{count}",
            $"FullSemanticSuccess={fullSuccessCount}/{count}",
            $"QwenInvoked={qwenCount}/{count}",
            $"DeterministicBypass={bypassCount}",
            $"CorrectBypassRatio={correctBypassCount}/{bypassCount}",
            $"Clarification={clarificationCount}",
            $"UnsupportedCorrect={unsupportedCorrectCount}/{cases.Count(item => item.Unsupported)}",
            $"UnsupportedQwenInvoked={unsupportedQwenCount}",
            $"UnsafeSubstitution={unsafeSubstitutionCount}",
            $"UnsupportedQueryBuilder={unsupportedQueryBuilderCount}",
            $"UnsupportedSql={unsupportedSqlCount}",
            $"AverageDeterministicParserMilliseconds={Average(deterministicParserMilliseconds, count)}",
            $"AverageIntentDetectionMilliseconds={Average(intentMilliseconds, count)}",
            $"AveragePreprocessingMilliseconds={Average(preprocessingMilliseconds, count)}",
            $"AverageEmbeddingMilliseconds={Average(embeddingMilliseconds, count)}",
            $"AverageSimilarityMilliseconds={Average(similarityMilliseconds, count)}",
            $"AverageCompletenessGateMilliseconds={Average(completenessMilliseconds, count)}",
            $"AverageAssemblyMilliseconds={Average(assemblyMilliseconds, count)}",
            $"AveragePartialQwenMilliseconds={Average(qwenMilliseconds, qwenCount)}",
            $"AverageValidationMilliseconds={Average(validationMilliseconds, count)}",
            $"AverageTotalPlanningMilliseconds={summary.AverageTotalMilliseconds}"));
        Console.WriteLine(string.Join(" | ",
            $"Suite={suite}QualitySummary",
            $"SupportedSemanticSuccess={supportedSuccessCount}/{supportedCount}",
            $"FilterAccuracy={filterCorrectCount}/{filterMeasuredCount}",
            $"SourceAccuracy={sourceCorrectCount}/{sourceMeasuredCount}",
            $"CandidateRecall={candidateRecallCount}/{candidateRecallMeasuredCount}",
            $"AmbiguousSafeClarification={ambiguousSafeCount}/{ambiguousCount}",
            $"AmbiguousUnsafeAccepted={ambiguousUnsafeAcceptedCount}",
            $"AverageEmbeddingOnlyMilliseconds={Average(embeddingOnlyDurations)}",
            $"AverageQwenAssistedMilliseconds={Average(qwenAssistedDurations)}",
            $"P50Milliseconds={Percentile(totalDurations, .50)}",
            $"P95Milliseconds={Percentile(totalDurations, .95)}"));
        return summary;
    }

    private static long Average(long total, int count) => count == 0 ? 0 : total / count;

    private static string FormatEvidence(SemanticResolutionResult? result) =>
        result?.CandidateEvidence is null ? "None" : string.Join(',',
            result.CandidateEvidence.Take(3).Select(candidate => string.Join(':',
                candidate.SemanticKey,
                candidate.FinalScore.ToString("0.000"),
                candidate.EmbeddingScore.ToString("0.000"),
                candidate.LexicalEvidenceScore.ToString("0.000"),
                candidate.PhraseCoverage.ToString("0.000"),
                candidate.DescriptionRelevance.ToString("0.000"))));

    private static long Average(IReadOnlyCollection<long> values) =>
        values.Count == 0 ? 0 : (long)values.Average();

    private static long Percentile(IReadOnlyCollection<long> values, double percentile)
    {
        if (values.Count == 0)
        {
            return 0;
        }
        var ordered = values.Order().ToArray();
        var index = (int)Math.Ceiling(percentile * ordered.Length) - 1;
        return ordered[Math.Clamp(index, 0, ordered.Length - 1)];
    }

    private static bool CanResolveWithPartialQwen(SemanticPlanningState state)
    {
        if (state.UnsupportedSlots.Count > 0)
        {
            return false;
        }
        var unresolved = state.AmbiguousSlots.Select(slot => slot.Kind)
            .Concat(state.MissingSlots).Distinct().ToArray();
        return unresolved.Length > 0
            && unresolved.All(kind => kind is UnresolvedConceptKind.Metric
                or UnresolvedConceptKind.Dimension
                or UnresolvedConceptKind.Filter)
            && unresolved.All(kind => state.QwenResolvableSlots.Any(slot =>
                slot.Kind == kind && slot.CandidateKeys.Count > 0));
    }

    private static SemanticCandidateConstraints GapConstraints(
        SemanticPlanningState state) => new(
        state.QwenResolvableSlots
            .Where(slot => slot.Kind == UnresolvedConceptKind.Metric)
            .SelectMany(slot => slot.CandidateKeys)
            .Concat(state.ResolvedMetric is null ? [] : [state.ResolvedMetric])
            .Distinct(StringComparer.Ordinal).ToArray(),
        state.QwenResolvableSlots
            .Where(slot => slot.Kind is UnresolvedConceptKind.Dimension
                or UnresolvedConceptKind.Filter)
            .SelectMany(slot => slot.CandidateKeys)
            .Concat(state.ResolvedDimensions)
            .Distinct(StringComparer.Ordinal).ToArray(),
        state.ResolvedDate,
        state.ResolvedMetric is null ? [] : [state.ResolvedMetric],
        state.ResolvedDimensions,
        state.QwenResolvableSlots.Select(slot => slot.Kind).ToArray(),
        state.ResolvedSource);

    private static bool GapSelectionAllowed(
        SemanticGapSelection selection,
        SemanticPlanningState state,
        SemanticCandidateConstraints constraints)
    {
        var metricGap = state.QwenResolvableSlots.Any(slot =>
            slot.Kind == UnresolvedConceptKind.Metric);
        var dimensionGap = state.QwenResolvableSlots.Any(slot =>
            slot.Kind is UnresolvedConceptKind.Dimension
                or UnresolvedConceptKind.Filter);
        return metricGap == (selection.MetricKey is not null)
            && (selection.MetricKey is null || constraints.MetricKeys.Contains(
                selection.MetricKey, StringComparer.Ordinal))
            && dimensionGap == (selection.DimensionKeys.Count > 0)
            && selection.DimensionKeys.All(key => constraints.DimensionKeys.Contains(
                key, StringComparer.Ordinal));
    }

    private static SemanticResolverResult CompleteSemantic(
        SemanticResolverResult original,
        SemanticPlanningState state,
        SemanticGapSelection selection)
    {
        var metric = original.Metric;
        if (selection.MetricKey is not null)
        {
            metric = Resolved(original.Metric, SemanticSlotKind.Metric,
                selection.MetricKey, state.Confidence);
        }
        var dimension = original.Dimension;
        if (selection.DimensionKeys.Count > 0
            && state.QwenResolvableSlots.Any(slot =>
                slot.Kind == UnresolvedConceptKind.Dimension))
        {
            dimension = Resolved(original.Dimension, SemanticSlotKind.Dimension,
                selection.DimensionKeys[0], state.Confidence);
        }
        var filterLiteral = original.FilterLiteral;
        if (selection.DimensionKeys.Count > 0
            && state.QwenResolvableSlots.Any(slot =>
                slot.Kind == UnresolvedConceptKind.Filter)
            && filterLiteral is not null)
        {
            filterLiteral = filterLiteral with
            {
                DimensionCandidate = selection.DimensionKeys[0],
                ResolutionKind = FilterLiteralResolutionKind.Resolved,
                DimensionCandidateKeys = selection.DimensionKeys
            };
        }
        return original with
        {
            Metric = metric,
            Dimension = dimension,
            FilterLiteral = filterLiteral
        };
    }

    private static SemanticResolutionResult Resolved(
        SemanticResolutionResult? original,
        SemanticSlotKind kind,
        string key,
        double confidence) => new(
        SemanticResolutionKind.Resolved, kind, key,
        original?.Similarity ?? confidence,
        original?.SecondBestSimilarity ?? -1,
        original?.Margin ?? 0,
        [key],
        original?.SupportingRepresentationType
            ?? SemanticRepresentationType.FullNormalized);

    private static UnresolvedConceptKind FirstUnresolved(
        SemanticPlanningState state) => state.UnsupportedSlots
        .Concat(state.MissingSlots)
        .Concat(state.AmbiguousSlots.Select(slot => slot.Kind))
        .FirstOrDefault(UnresolvedConceptKind.Metric);

    private static OllamaStructuredPlanningResult AcceptedPlan(
        CanonicalRequest canonical,
        string outcome,
        long duration = 0,
        int? promptTokens = 0,
        int? completionTokens = 0) => new(
        new PlanningResult(PlanningOutcome.Accepted, canonical, [], null),
        outcome, "NONE", duration, true, null, promptTokens, completionTokens);

    private static OllamaStructuredPlanningResult NonAcceptedPlan(
        PlanningOutcome outcome,
        UnresolvedConceptKind kind,
        long duration = 0) => new(
        new PlanningResult(outcome, null, [new UnresolvedConcept(kind)],
            outcome == PlanningOutcome.NeedsClarification
                ? new PlanningClarification(kind) : null),
        outcome.ToString(), "NONE", duration, true, null, 0, 0);
}
