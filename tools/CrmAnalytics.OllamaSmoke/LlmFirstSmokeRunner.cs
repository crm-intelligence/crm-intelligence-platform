using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.Service;
using CrmAnalytics.Infrastructure.Integrations;

namespace CrmAnalytics.OllamaSmoke;

internal sealed record LlmFirstSmokeCase(
    string Id,
    string Prompt,
    string? Metric,
    string? Dimension,
    DateRangeKind? DateKind,
    bool Unsupported = false);

internal sealed class LlmFirstSmokeRunner(
    IOllamaStructuredPlanningClient ollama,
    ISqlProductionService production,
    ILlmFirstCanonicalRequestAssembler assembler,
    DateOnly today)
{
    public async Task<int> RunAsync(
        int start,
        int count,
        CancellationToken cancellationToken)
    {
        var supportedPassed = 0;
        var supportedTotal = 0;
        var safetyPassed = 0;
        var safetyTotal = 0;
        var safetyQueryBuilderCalls = 0;
        var safetySqlGenerated = 0;

        foreach (var item in Cases().Skip(start).Take(count))
        {
            var requestId = $"llm-first-smoke-{item.Id}";
            var result = await ollama.PlanSemanticAsync(
                new OllamaSemanticPlanningRequest(
                    requestId, "llm-first-small", today, item.Prompt),
                cancellationToken);
            var queryBuilderCalled = false;
            var sqlGenerated = false;
            CanonicalRequest? canonical = null;
            var backendReason = "NONE";
            var finalOutcome = result.Plan?.Outcome.ToString()
                ?? $"{result.Outcome}:{result.ReasonCode}";

            if (result.Plan is
                { Outcome: PlanningOutcome.Accepted, Intent: not null } accepted)
            {
                var assembly = assembler.Assemble(
                    requestId, "llm-first-small", item.Prompt, today, null, null,
                    accepted.Intent);
                finalOutcome = assembly.Outcome.ToString();
                backendReason = assembly.ReasonCode;
                if (assembly is
                    { Outcome: LlmFirstAssemblyOutcome.Assembled,
                        CanonicalRequest: not null })
                {
                    canonical = assembly.CanonicalRequest;
                    queryBuilderCalled = true;
                    var produced = production.ProduceCanonical(
                        new SqlProductionRequest
                        {
                            RequestId = requestId,
                            ConversationId = "llm-first-small",
                            Prompt = item.Prompt,
                            Today = today,
                            Scope = UserDataScope.Unrestricted,
                            Source = null,
                            UserId = "smoke-redacted"
                        },
                        canonical);
                    sqlGenerated = produced.Decision == GuardrailDecision.Accepted
                        && !string.IsNullOrWhiteSpace(produced.Sql);
                    finalOutcome = produced.Decision.ToString();
                }
            }

            if (item.Unsupported)
            {
                safetyTotal++;
                var safe = !queryBuilderCalled && !sqlGenerated
                    && canonical is null;
                safetyQueryBuilderCalls += queryBuilderCalled ? 1 : 0;
                safetySqlGenerated += sqlGenerated ? 1 : 0;
                safetyPassed += safe ? 1 : 0;
                Console.WriteLine(string.Join(" | ",
                    $"Case={item.Id}",
                    "Expected=UnsupportedSafe",
                    $"Outcome={finalOutcome}",
                    $"QueryBuilderCalled={queryBuilderCalled}",
                    $"SqlGenerated={sqlGenerated}",
                    $"BackendReason={backendReason}",
                    $"DoneReason={result.DoneReason ?? "none"}",
                    $"CompletionTokens={result.CompletionTokenCount?.ToString() ?? "none"}",
                    $"Safe={safe}"));
                continue;
            }

            supportedTotal++;
            var correct = canonical is not null
                && canonical.Metrics.SequenceEqual([item.Metric!])
                && (item.Dimension is null
                    ? canonical.Dimensions.Count == 0
                    : canonical.Dimensions.SequenceEqual([item.Dimension]))
                && canonical.DateRange.Kind == item.DateKind
                && queryBuilderCalled
                && sqlGenerated;
            supportedPassed += correct ? 1 : 0;
            Console.WriteLine(string.Join(" | ",
                $"Case={item.Id}",
                $"Outcome={finalOutcome}",
                $"Metric={canonical?.Metrics.SingleOrDefault() ?? "none"}",
                $"Dimension={canonical?.Dimensions.SingleOrDefault() ?? "none"}",
                $"DateKind={canonical?.DateRange.Kind.ToString() ?? "none"}",
                $"Source={canonical?.Source?.ToString() ?? "none"}",
                $"QueryBuilderCalled={queryBuilderCalled}",
                $"SqlGenerated={sqlGenerated}",
                $"BackendReason={backendReason}",
                $"DoneReason={result.DoneReason ?? "none"}",
                $"CompletionTokens={result.CompletionTokenCount?.ToString() ?? "none"}",
                $"Correct={correct}"));
        }

        Console.WriteLine(string.Join(" | ",
            "Suite=LlmFirstSmall",
            $"Supported={supportedPassed}/{supportedTotal}",
            $"Safety={safetyPassed}/{safetyTotal}",
            $"QueryBuilderForSafetyCases={safetyQueryBuilderCalls}",
            $"SqlForSafetyCases={safetySqlGenerated}"));
        return supportedPassed == supportedTotal && safetyPassed == safetyTotal
            ? 0 : 1;
    }

    private static IReadOnlyList<LlmFirstSmokeCase> Cases() =>
    [
        new("H01", "Kategoriler kiriliminda urunlerden gelen para akisini son otuz gun icin ozetle",
            "item_sales", "product_category", DateRangeKind.Relative),
        new("H02", "Gecen ay eyaletlere dagilan benzersiz order adedini ver",
            "order_count", "customer_state", DateRangeKind.Relative),
        new("H03", "2018-01-01 ile 2018-03-31 arasinda sehir sehir nakliye ucretleri",
            "freight_total", "customer_city", DateRangeKind.Absolute),
        new("H05", "Bu ceyrekte payment method bazinda tahsil edilen meblag",
            "payment_total", "payment_type", DateRangeKind.Relative),
        new("H06", "Ocak 2018 odemelerinde kart basina taksitlerin mean degeri",
            "avg_installments", "payment_type", DateRangeKind.Absolute),
        new("H09", "Bir alicinin ortalama order frequency degerini hesapla",
            "avg_frequency", null, DateRangeKind.NotApplicable),
        new("H12", "2018 boyunca urun satis bedeli",
            "item_sales", null, DateRangeKind.Absolute),
        new("H10", "Bu sene kategoriye ayrilmis satilan satir adedi",
            "item_count", "product_category", DateRangeKind.Relative),
        new("U23", "Gecen hafta urun kategorilerine dagilan siparis miktari",
            "order_count", "product_category", DateRangeKind.Relative),
        new("X01", "Bu yil reklam tiklama basina donusum orani",
            null, null, null, Unsupported: true),
        new("X03", "Gelecek ceyrek stok talep tahmini",
            null, null, null, Unsupported: true),
        new("X06", "Satici komisyon yuzdesini hesapla",
            null, null, null, Unsupported: true)
    ];
}
