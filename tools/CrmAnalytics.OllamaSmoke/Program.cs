using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using Crm.Analytics.Sql.Audit;
using Crm.Analytics.Sql.Catalog;
using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.Nlu;
using Crm.Analytics.Sql.Service;
using CrmAnalytics.Infrastructure.Integrations;
using CrmAnalytics.OllamaSmoke;
using Microsoft.Extensions.Options;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;

var enabled = bool.TryParse(Environment.GetEnvironmentVariable("Ollama__Enabled"),
    out var enabledValue) && enabledValue;
var evaluationSuite = Environment.GetEnvironmentVariable("OllamaSmoke__Suite")
    ?? "All";

var options = new OllamaOptions
{
    Enabled = true,
    BaseUrl = Environment.GetEnvironmentVariable("Ollama__BaseUrl")
        ?? "http://localhost:11434",
    Model = Environment.GetEnvironmentVariable("Ollama__Model") ?? "qwen3:8b",
    TimeoutSeconds = int.TryParse(Environment.GetEnvironmentVariable(
        "Ollama__TimeoutSeconds"), out var ollamaTimeoutSeconds)
            ? ollamaTimeoutSeconds : 120,
    Temperature = 0
};
var embeddingOptions = new SemanticEmbeddingOptions
{
    Enabled = true,
    BaseUrl = Environment.GetEnvironmentVariable("SemanticEmbedding__BaseUrl")
        ?? "http://localhost:11434",
    Model = Environment.GetEnvironmentVariable("SemanticEmbedding__Model")
        ?? "qwen3-embedding:0.6b",
    TimeoutSeconds = 15,
    CandidateCount = int.TryParse(Environment.GetEnvironmentVariable(
        "SemanticEmbedding__CandidateCount"), out var candidateCount)
            ? candidateCount : 3,
    MinSimilarity = double.TryParse(Environment.GetEnvironmentVariable(
        "SemanticEmbedding__MinSimilarity"), out var minSimilarity)
            ? minSimilarity : .70,
    AmbiguityMargin = double.TryParse(Environment.GetEnvironmentVariable(
        "SemanticEmbedding__AmbiguityMargin"), out var ambiguityMargin)
            ? ambiguityMargin : .02,
    PartialQwenMinSimilarity = .55,
    MinLexicalEvidenceForBypass = 2,
    UnsupportedHighMargin = .05
};
var repositoryRoot = Environment.GetEnvironmentVariable(
        "OllamaSmoke__RepositoryRoot")
    ?? EvaluationBuildParity.FindRepositoryRoot(AppContext.BaseDirectory);
var parity = EvaluationBuildParity.Verify(
    repositoryRoot, GeneratedEvaluationBuild.RepositoryFingerprint);
var binaryPath = Assembly.GetExecutingAssembly().Location;
var binarySha = Convert.ToHexString(SHA256.HashData(
    File.ReadAllBytes(binaryPath))).ToLowerInvariant();
Console.WriteLine(string.Join(" | ",
    "Suite=ActiveConfiguration",
    $"MinSimilarity={embeddingOptions.MinSimilarity:0.00}",
    $"AmbiguityMargin={embeddingOptions.AmbiguityMargin:0.00}",
    $"CandidateCount={embeddingOptions.CandidateCount}",
    $"RetrievalPoolSize={embeddingOptions.RetrievalPoolSize}",
    $"Model={options.Model}",
    $"EmbeddingModel={embeddingOptions.Model}"));
Console.WriteLine(string.Join(" | ",
    "Suite=BuildFingerprint",
    $"Configuration={GeneratedEvaluationBuild.Configuration}",
    $"BuildTimestampUtc={GeneratedEvaluationBuild.BuildTimestampUtc}",
    $"BinarySha256={binarySha}",
    $"BuildRepositoryFingerprint={parity.ExpectedFingerprint}",
    $"CurrentRepositoryFingerprint={parity.RepositoryFingerprint}",
    $"Match={parity.IsMatch}"));
if (!string.Equals(GeneratedEvaluationBuild.Configuration, "Release",
        StringComparison.Ordinal)
    || !parity.IsMatch)
{
    Console.Error.WriteLine(
        $"Evaluation refused: {parity.FailureKind ?? "NonReleaseBuild"}.");
    return 3;
}
if (!enabled)
{
    Console.Error.WriteLine("Ollama__Enabled=true is required.");
    return 2;
}
using var httpClient = new HttpClient
{
    BaseAddress = new Uri(options.BaseUrl),
    Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds)
};
var registry = SemanticCatalogRegistry.CreateDefault();
var contract = new OllamaCanonicalContract(registry);
var ollama = new OllamaStructuredPlanningClient(
    httpClient, Options.Create(options), contract);
var production = SqlProductionFactory.CreateForOlist(
    new NullAuditWriter(), semanticCatalogs: registry);
var today = DateOnly.FromDateTime(DateTime.UtcNow);
if (evaluationSuite.Equals("LlmFirstSmall", StringComparison.OrdinalIgnoreCase))
{
    var llmFirstStart = int.TryParse(Environment.GetEnvironmentVariable(
        "OllamaSmoke__Start"), out var llmFirstConfiguredStart)
            ? Math.Max(llmFirstConfiguredStart, 0) : 0;
    var llmFirstCount = int.TryParse(Environment.GetEnvironmentVariable(
        "OllamaSmoke__Count"), out var llmFirstConfiguredCount)
            ? Math.Max(llmFirstConfiguredCount, 1) : int.MaxValue;
    return await new LlmFirstSmokeRunner(
        ollama,
        production,
        new LlmFirstCanonicalRequestAssembler(registry),
        today).RunAsync(llmFirstStart, llmFirstCount, CancellationToken.None);
}
using var embeddingHttpClient = new HttpClient
{
    BaseAddress = new Uri(embeddingOptions.BaseUrl),
    Timeout = TimeSpan.FromSeconds(embeddingOptions.TimeoutSeconds)
};
var embeddingClient = new OllamaSemanticEmbeddingClient(
    embeddingHttpClient, Options.Create(embeddingOptions));
using var indexEmbeddingHttpClient = new HttpClient
{
    BaseAddress = new Uri(embeddingOptions.BaseUrl),
    Timeout = TimeSpan.FromSeconds(120)
};
var indexEmbeddingClient = new OllamaSemanticEmbeddingClient(
    indexEmbeddingHttpClient, Options.Create(embeddingOptions));
var embeddingIndex = new SemanticEmbeddingIndex(
    new SemanticDocumentFactory(registry), indexEmbeddingClient,
    Options.Create(embeddingOptions));
var resolver = new SemanticEmbeddingResolver(
    embeddingIndex, embeddingClient, Options.Create(embeddingOptions));
var assembler = new SemanticCanonicalRequestAssembler(registry);
var start = int.TryParse(Environment.GetEnvironmentVariable("OllamaSmoke__Start"),
    out var configuredStart) ? Math.Max(configuredStart, 0) : 0;
var count = int.TryParse(Environment.GetEnvironmentVariable("OllamaSmoke__Count"),
    out var configuredCount) ? Math.Max(configuredCount, 1) : int.MaxValue;
var calibrationOnly = bool.TryParse(Environment.GetEnvironmentVariable(
    "OllamaSmoke__CalibrationOnly"), out var calibrationOnlyValue)
    && calibrationOnlyValue;
var skipCalibration = bool.TryParse(Environment.GetEnvironmentVariable(
    "OllamaSmoke__SkipCalibration"), out var skipCalibrationValue)
    && skipCalibrationValue;
var skipWarmup = bool.TryParse(Environment.GetEnvironmentVariable(
    "OllamaSmoke__SkipWarmup"), out var skipWarmupValue) && skipWarmupValue;
using (var coldWarmupHttpClient = new HttpClient
{
    BaseAddress = new Uri(embeddingOptions.BaseUrl),
    Timeout = TimeSpan.FromSeconds(120)
})
{
    var coldWarmupClient = new OllamaSemanticEmbeddingClient(
        coldWarmupHttpClient, Options.Create(embeddingOptions));
    var coldTimer = Stopwatch.StartNew();
    await coldWarmupClient.EmbedAsync(["semantic resolver warmup"],
        CancellationToken.None);
    coldTimer.Stop();
    Console.WriteLine(string.Join(" | ",
        "Suite=EmbeddingModelColdWarmup",
        $"Model={embeddingOptions.Model}",
        $"DurationMilliseconds={coldTimer.ElapsedMilliseconds}"));
}

var catalogTimer = Stopwatch.StartNew();
await embeddingIndex.GetAsync(CancellationToken.None);
catalogTimer.Stop();
Console.WriteLine(string.Join(" | ",
    "Suite=EmbeddingCatalogWarmup",
    $"Model={embeddingOptions.Model}",
    $"DurationMilliseconds={catalogTimer.ElapsedMilliseconds}"));

if (!skipCalibration)
{
    await RunCalibrationAsync(
        resolver, contract, DevelopmentCalibrationCases(),
        UnsupportedCalibrationExamples(), embeddingOptions, today);
}
if (calibrationOnly)
{
    Console.WriteLine("Suite=CalibrationComplete | EvaluationExecuted=false");
    return 0;
}

// Warm-up is measured separately and excluded from planning latency statistics.
if (start == 0 && !skipWarmup)
{
    var warmupTimer = Stopwatch.StartNew();
    var warmupSemantic = await resolver.ResolveAsync(
        "2018 yilindaki siparis adedi", DataSource.Dwh, null,
        CancellationToken.None);
    var warmup = await ollama.PlanAsync(new OllamaStructuredPlanningRequest(
        "ollama-warmup-request", "ollama-warmup-conversation", today,
        "2018 yilindaki siparis adedi", CandidateConstraints:
        Constraints(warmupSemantic, "2018 yilindaki siparis adedi", today)),
        CancellationToken.None);
    warmupTimer.Stop();
    Console.WriteLine(string.Join(" | ",
        "Suite=Warmup",
        $"Outcome={warmup.Outcome}",
        $"DurationMilliseconds={warmupTimer.ElapsedMilliseconds}"));
}

var evaluation = new EvaluationRunner(
    resolver, ollama, production, assembler, today);
if (evaluationSuite.Equals("Regression", StringComparison.OrdinalIgnoreCase)
    || evaluationSuite.Equals("RegressionH", StringComparison.OrdinalIgnoreCase))
{
    await evaluation.RunAsync(
        "Regression", RegressionCases().Skip(start).Take(count).ToArray(),
        CancellationToken.None);
    return 0;
}
if (evaluationSuite.Equals("RegressionN", StringComparison.OrdinalIgnoreCase))
{
    await evaluation.RunAsync(
        "RegressionN", RegressionNCases().Skip(start).Take(count).ToArray(),
        CancellationToken.None);
    return 0;
}
if (evaluationSuite.Equals("Unsupported", StringComparison.OrdinalIgnoreCase))
{
    await evaluation.RunAsync(
        "Unsupported", UnsupportedCases(), CancellationToken.None);
    return 0;
}
if (evaluationSuite.Equals("NewUnseen", StringComparison.OrdinalIgnoreCase))
{
    await evaluation.RunAsync(
        "NewUnseen", NewWideUnseenCases().Skip(start).Take(count).ToArray(),
        CancellationToken.None);
    return 0;
}
var regression = await evaluation.RunAsync(
    "Regression", RegressionCases().Skip(start).Take(count).ToArray(),
    CancellationToken.None);
var unsupported = await evaluation.RunAsync(
    "Unsupported", UnsupportedCases(), CancellationToken.None);
var unseen = await evaluation.RunAsync(
    "NewUnseen", NewWideUnseenCases(), CancellationToken.None);

var regressionPassed = regression.FullSemanticSuccess > 5
    && regression.HasSafeBypass;
var unsupportedPassed = unsupported.HasSafeUnsupported;
var unseenPassed = unseen.HasSafeBypass && unseen.HasSafeUnsupported
    && unseen.FullSemanticSuccess > 0;
return regressionPassed && unsupportedPassed && unseenPassed ? 0 : 1;

static SemanticCandidateConstraints Constraints(
    SemanticResolverResult result,
    string prompt,
    DateOnly today) => new(
    CandidateKeys(result.Metric, result.MetricRequested),
    CandidateKeys(result.Dimension, result.DimensionRequested),
    RelativeDateResolver.Resolve(TurkishTextNormalizer.Tokenize(prompt), today)?.Range,
    result.Metric?.Kind == SemanticResolutionKind.Resolved
        && result.Metric.CandidateKey is not null
            ? [result.Metric.CandidateKey] : [],
    result.Dimension?.Kind == SemanticResolutionKind.Resolved
        && result.Dimension.CandidateKey is not null
            ? [result.Dimension.CandidateKey] : []);

static IReadOnlyList<string> CandidateKeys(
    SemanticResolutionResult? result,
    bool requested) => !requested || result is null
        ? []
        : result.Kind == SemanticResolutionKind.Resolved
            && result.CandidateKey is not null
                ? [result.CandidateKey]
                : result.CandidateKeys;

static async Task RunCalibrationAsync(
    ISemanticEmbeddingResolver resolver,
    OllamaCanonicalContract contract,
    IReadOnlyList<QualityCase> cases,
    IReadOnlyList<string> unsupportedConcepts,
    SemanticEmbeddingOptions configured,
    DateOnly today)
{
    var supported = new List<(SemanticResolutionResult Result, string Expected)>();
    var constrainedCatalogCharacters = new List<int>();
    foreach (var item in cases)
    {
        var result = await resolver.ResolveAsync(
            item.Prompt, DataSource.Dwh, null, today, CancellationToken.None);
        if (item.Metric is not null && result.Metric is not null)
        {
            supported.Add((result.Metric, item.Metric));
        }
        if (item.Dimension is not null && result.Dimension is not null)
        {
            supported.Add((result.Dimension, item.Dimension));
        }
        constrainedCatalogCharacters.Add(contract.CreateCatalogPrompt(
            Constraints(result, item.Prompt, today)).Length);
    }

    var fullCatalogCharacters = contract.CreateCatalogPrompt().Length;
    var averageConstrainedCharacters = (int)constrainedCatalogCharacters.Average();
    Console.WriteLine(string.Join(" | ",
        "Suite=CatalogReduction",
        $"FullCatalogCharacters={fullCatalogCharacters}",
        $"AverageConstrainedCatalogCharacters={averageConstrainedCharacters}",
        $"Reduction={(1d - (double)averageConstrainedCharacters / fullCatalogCharacters):P1}"));

    var unsupported = new List<SemanticResolutionResult>();
    foreach (var concept in unsupportedConcepts)
    {
        var result = await resolver.ResolveAsync(
            concept, DataSource.Dwh, null, today, CancellationToken.None);
        if (result.Metric is not null)
        {
            unsupported.Add(result.Metric);
        }
    }

    var configuredResolved = supported.Where(item =>
        item.Result.Kind == SemanticResolutionKind.Resolved).ToArray();
    var configuredCorrect = configuredResolved.Count(item =>
        item.Result.CandidateKey == item.Expected);
    var configuredFalsePositive = unsupported.Count(item =>
        item.Kind == SemanticResolutionKind.Resolved);
    Console.WriteLine(string.Join(" | ",
        "Suite=ConfiguredResolverCalibration",
        $"ResolvedPrecision={(configuredResolved.Length == 0 ? 0 : (double)configuredCorrect / configuredResolved.Length):P1}",
        $"ResolvedRecall={(supported.Count == 0 ? 0 : (double)configuredCorrect / supported.Count):P1}",
        $"UnsupportedFalsePositiveRate={(unsupported.Count == 0 ? 0 : (double)configuredFalsePositive / unsupported.Count):P1}",
        $"PartialQwenMinSimilarity={configured.PartialQwenMinSimilarity:0.00}",
        $"MinLexicalEvidenceForBypass={configured.MinLexicalEvidenceForBypass}"));

    double[] similarities = [.45, .50, .55, .60, .65, .70];
    double[] margins = [.02, .05, .08, .10];
    foreach (var threshold in similarities)
    foreach (var margin in margins)
    {
        var resolved = supported.Where(item => IsResolved(item.Result,
            threshold, margin)).ToArray();
        var correct = resolved.Count(item => item.Result.CandidateKeys.FirstOrDefault()
            == item.Expected);
        var ambiguous = supported.Count(item => item.Result.Similarity >= threshold
            && item.Result.Margin < margin);
        var falsePositive = unsupported.Count(item => IsResolved(
            item, threshold, margin));
        Console.WriteLine(string.Join(" | ",
            "Suite=ThresholdCalibration",
            $"MinSimilarity={threshold:0.00}",
            $"AmbiguityMargin={margin:0.00}",
            $"SupportedPrecision={(resolved.Length == 0 ? 0 : (double)correct / resolved.Length):P1}",
            $"SupportedRecall={(supported.Count == 0 ? 0 : (double)correct / supported.Count):P1}",
            $"UnsupportedFalsePositiveRate={(unsupported.Count == 0 ? 0 : (double)falsePositive / unsupported.Count):P1}",
            $"AmbiguityRate={(supported.Count == 0 ? 0 : (double)ambiguous / supported.Count):P1}",
            $"Configured={threshold == configured.MinSimilarity && margin == configured.AmbiguityMargin}"));
    }
}

static bool IsResolved(
    SemanticResolutionResult result,
    double minSimilarity,
    double ambiguityMargin) => result.Similarity >= minSimilarity
    && result.Margin >= ambiguityMargin;

static IReadOnlyList<QualityCase> DevelopmentCalibrationCases() =>
[
    new("C01", "2019 satilan mallarin fiyat toplami", "item_sales", null,
        DateRangeKind.Absolute, "Accepted"),
    new("C02", "2019 alicilarin urun ve kargo odemesi", "customer_paid_total", null,
        DateRangeKind.Absolute, "Accepted"),
    new("C03", "2019 teslimat icin alinan ucret toplami", "freight_total", null,
        DateRangeKind.Absolute, "Accepted"),
    new("C04", "2019 kac tekil siparis olustu", "order_count", null,
        DateRangeKind.Absolute, "Accepted"),
    new("C05", "2019 siparis satirlarinin adedi", "item_count", null,
        DateRangeKind.Absolute, "Accepted"),
    new("C06", "2019 siparis basina urun degeri", "avg_basket", null,
        DateRangeKind.Absolute, "Accepted"),
    new("C07", "2019 tahsil edilen odeme toplami", "payment_total", null,
        DateRangeKind.Absolute, "Accepted"),
    new("C08", "2019 odeme basina taksit adedi", "avg_installments", null,
        DateRangeKind.Absolute, "Accepted"),
    new("C09", "kac farkli alici bulunuyor", "customer_count", null,
        DateRangeKind.NotApplicable, "Accepted"),
    new("C10", "bir alicinin ortalama urun harcamasi", "avg_monetary", null,
        DateRangeKind.NotApplicable, "Accepted"),
    new("C11", "alici basina dusen siparis adedi", "avg_frequency", null,
        DateRangeKind.NotApplicable, "Accepted"),
    new("C12", "2019 urun gruplarina gore satis meblagi", "item_sales",
        "product_category", DateRangeKind.Absolute, "Accepted"),
    new("C13", "2019 alici sehrine gore siparis adedi", "order_count",
        "customer_city", DateRangeKind.Absolute, "Accepted")
];

static IReadOnlyList<string> UnsupportedCalibrationExamples() =>
[
    "brut kazanc yuzdesi",
    "gelecek donem stok ihtiyaci",
    "reklam harcamasinin donus performansi"
];

static IReadOnlyList<QualityCase> RegressionCases() =>
[
    new("H01", "Kategoriler kiriliminda urunlerden gelen para akisini son otuz gun icin ozetle",
        "item_sales", "product_category", DateRangeKind.Relative, "Accepted"),
    new("H02", "Gecen ay eyaletlere dagilan benzersiz order adedini ver",
        "order_count", "customer_state", DateRangeKind.Relative, "Accepted"),
    new("H03", "2018-01-01 ile 2018-03-31 arasinda sehir sehir nakliye ucretleri",
        "freight_total", "customer_city", DateRangeKind.Absolute, "Accepted"),
    new("H04", "Bu ceyrekte bir siparisin ortalama urun degeri nedir",
        "avg_basket", null, DateRangeKind.Relative, "Accepted"),
    new("H05", "Bu yil payment method bazinda tahsil edilen meblag",
        "payment_total", "payment_type", DateRangeKind.Relative, "Accepted"),
    new("H06", "Ocak 2018 odemelerinde kart basina taksitlerin mean degeri",
        "avg_installments", "payment_type", DateRangeKind.Absolute, "Accepted"),
    new("H07", "Musteri eyaletlerinde kac farkli alici var",
        "customer_count", "rfm_customer_state", DateRangeKind.NotApplicable, "Accepted"),
    new("H08", "Customer city kiriliminda kisi basi average spend",
        "avg_monetary", "rfm_customer_city", DateRangeKind.NotApplicable, "Accepted"),
    new("H09", "Bir alicinin ortalama order frequency degerini hesapla",
        "avg_frequency", null, DateRangeKind.NotApplicable, "Accepted"),
    new("H10", "Bu sene kategoriye ayrilmis satilan satir adedi",
        "item_count", "product_category", DateRangeKind.Relative, "Accepted"),
    new("H11", "SP ile RJ eyaletlerini bu ay siparis adediyle kiyasla",
        "order_count", "customer_state", DateRangeKind.Relative, "Accepted"),
    new("H12", "2018 boyunca urun satis bedeli",
        "item_sales", null, DateRangeKind.Absolute, "Accepted")
];

static IReadOnlyList<QualityCase> UnsupportedCases() =>
[
    new("S01", "2018 yilinda faaliyet kari marji",
        null, null, DateRangeKind.Absolute, "Unsupported", Unsupported: true),
    new("S02", "2027 yili talep tahmini",
        null, null, DateRangeKind.Absolute, "Unsupported", Unsupported: true),
    new("S03", "Kampanya yatirim getirisi",
        null, null, null, "Unsupported", Unsupported: true)
];

static IReadOnlyList<QualityCase> RegressionNCases() =>
[
    new("N01", "Urun kategorilerinin gecen haftaki toplam kargo bedellerini listele",
        "freight_total", "product_category", DateRangeKind.Relative, "Accepted"),
    new("N02", "Bu sene sehirler bazinda musterinin odedigi miktari ozetle",
        "customer_paid_total", "customer_city", DateRangeKind.Relative, "Accepted"),
    new("N03", "Son 45 gunde siparis durumlarina gore kac kalem satildi",
        "item_count", "order_status", DateRangeKind.Relative, "Accepted"),
    new("N04", "Eyalet bazli son 6 aylik siparis sayilarini dok",
        "order_count", "customer_state", DateRangeKind.Relative, "Accepted"),
    new("N05", "Odeme sekillerine gore bu haftanin toplam tahsilati",
        "payment_total", "payment_type", DateRangeKind.Relative, "Accepted"),
    new("N06", "Gecen sene odeme yontemi kiriliminda ortalama taksit",
        "avg_installments", "payment_type", DateRangeKind.Relative, "Accepted"),
    new("N07", "Alici sehirlerinin ortalama parasal degerini hesapla",
        "avg_monetary", "rfm_customer_city", DateRangeKind.NotApplicable, "Accepted"),
    new("N08", "Eyaletlere gore tekil alici miktarini ver",
        "customer_count", "rfm_customer_state", DateRangeKind.NotApplicable, "Accepted"),
    new("N09", "Sehir bazinda alisveris sikligi ortalamasi",
        "avg_frequency", "rfm_customer_city", DateRangeKind.NotApplicable, "Accepted"),
    new("N10", "Dun sehir kiriliminda urun fiyat toplamini goster",
        "item_sales", "customer_city", DateRangeKind.Relative, "Accepted"),
    new("N11", "Son iki ayda teslim ve iptal durumlarini siparis adediyle karsilastir",
        "order_count", "order_status", DateRangeKind.Relative, "Accepted"),
    new("N12", "2018 boyunca eyalet eyalet ortalama sepet tutari",
        "avg_basket", "customer_state", DateRangeKind.Absolute, "Accepted"),
    new("N13", "Bu ay iade dolandiricilik risk skorunu hesapla",
        null, null, DateRangeKind.Relative, "Unsupported", Unsupported: true),
    new("N14", "Satici komisyon marjini sehir bazinda goster",
        null, null, null, "Unsupported", Unsupported: true),
    new("N15", "Son 90 gun web oturumu basina donusum orani",
        null, null, DateRangeKind.Relative, "Unsupported", Unsupported: true)
];

// Frozen holdout: added only after production semantic behavior and H/N regression
// work were complete. These sentences are not used by catalog aliases, prompts or
// calibration and must remain evaluation-only.
static IReadOnlyList<QualityCase> NewWideUnseenCases() =>
[
    new("U01", "Gecen ay tamamlanan siparislerin adedini soyle",
        "order_count", null, DateRangeKind.Relative, "Accepted"),
    new("U02", "Son ceyrekte kategori kirilimli urun gelirini getir",
        "item_sales", "product_category", DateRangeKind.Relative, "Accepted"),
    new("U03", "2018-01-01 ile 2018-06-30 arasinda sehir bazli tasima masrafi toplami",
        "freight_total", "customer_city", DateRangeKind.Absolute, "Accepted"),
    new("U04", "Son otuz gunde siparis basina ortalama urun bedeli",
        "avg_basket", null, DateRangeKind.Relative, "Accepted"),
    new("U05", "Bu yil odeme turlerine ayrilmis tahsilat hacmi",
        "payment_total", "payment_type", DateRangeKind.Relative, "Accepted"),
    new("U06", "2018 Mart ayinda odeme yontemine gore mean taksit sayisi",
        "avg_installments", "payment_type", DateRangeKind.Absolute, "Accepted"),
    new("U07", "Toplam benzersiz musteri miktari nedir",
        "customer_count", null, DateRangeKind.NotApplicable, "Accepted"),
    new("U08", "Musteri sehri kiriliminda kisi basi parasal ortalama",
        "avg_monetary", "rfm_customer_city", DateRangeKind.NotApplicable, "Accepted"),
    new("U09", "Musteri eyaletlerine gore average purchase frequency",
        "avg_frequency", "rfm_customer_state", DateRangeKind.NotApplicable, "Accepted"),
    new("U10", "Son 45 gunde durumlara dagilan satilmis kalem sayisi",
        "item_count", "order_status", DateRangeKind.Relative, "Accepted"),
    new("U11", "Bu sene eyalet bazinda alicidan tahsil edilen toplam",
        "customer_paid_total", "customer_state", DateRangeKind.Relative, "Accepted"),
    new("U12", "SP eyalet filtresinde son 30 gun siparis sayisi",
        "order_count", null, DateRangeKind.Relative, "Accepted",
        MeasureFilter: true, FilterDimension: "customer_state", FilterLiteralCount: 1),
    new("U13", "SP ve RJ eyaletlerini bu yil siparis adediyle karsilastir",
        "order_count", "customer_state", DateRangeKind.Relative, "Accepted",
        MeasureFilter: true, FilterDimension: "customer_state", FilterLiteralCount: 2),
    new("U14", "Istanbul sehir filtresinde gecen hafta urun satis tutari",
        "item_sales", null, DateRangeKind.Relative, "Accepted",
        MeasureFilter: true, FilterDimension: "customer_city", FilterLiteralCount: 1),
    new("U15", "Elektronik urun kategorisi filtresinde 2018 satis bedeli",
        "item_sales", null, DateRangeKind.Absolute, "Accepted",
        MeasureFilter: true, FilterDimension: "product_category", FilterLiteralCount: 1),
    new("U16", "\"delivered\" ve \"canceled\" siparis durumlarini gecen ay siparis adediyle kiyasla",
        "order_count", "order_status", DateRangeKind.Relative, "Accepted",
        MeasureFilter: true, FilterDimension: "order_status", FilterLiteralCount: 2),
    new("U17", "Dun sehir bazli urun gelir toplami",
        "item_sales", "customer_city", DateRangeKind.Relative, "Accepted"),
    new("U18", "Son kirk bes gunde urun gruplarina gore kargo maliyeti",
        "freight_total", "product_category", DateRangeKind.Relative, "Accepted"),
    new("U19", "Bu yil payment type by total collected value",
        "payment_total", "payment_type", DateRangeKind.Relative, "Accepted"),
    new("U20", "Siparis durumlarinin son iki aydaki kalem adetleri",
        "item_count", "order_status", DateRangeKind.Relative, "Accepted"),
    new("U21", "RFM customer city bazinda tekil musteri adedi",
        "customer_count", "rfm_customer_city", DateRangeKind.NotApplicable, "Accepted"),
    new("U22", "2018 boyunca toplam odeme degeri",
        "payment_total", null, DateRangeKind.Absolute, "Accepted"),
    new("U23", "Gecen hafta urun kategorilerine dagilan siparis miktari",
        "order_count", "product_category", DateRangeKind.Relative, "Accepted"),
    new("U24", "2018-01-01 ile 2018-01-31 arasindaki operasyonel siparisleri listele",
        null, "operational_order", DateRangeKind.Absolute, "Accepted",
        RequestSource: DataSource.Oltp, ExpectedSource: DataSource.Oltp),

    new("A01", "Gecen ay toplam sonucu goster",
        null, null, DateRangeKind.Relative, "NeedsClarification", Ambiguous: true,
        ExpectedSource: null),
    new("A02", "Musteri raporu hazirla",
        null, null, null, "NeedsClarification", Ambiguous: true,
        ExpectedSource: null),
    new("A03", "SP ve RJ icin siparis sayisini kiyasla",
        "order_count", null, null, "NeedsClarification", Ambiguous: true,
        MeasureFilter: true, FilterLiteralCount: 2, ExpectedSource: null),
    new("A04", "Istanbul icin bu ay toplam tutar",
        null, null, DateRangeKind.Relative, "NeedsClarification", Ambiguous: true,
        MeasureFilter: true, FilterLiteralCount: 1, ExpectedSource: null),
    new("A05", "Sehir bazinda sonucu ver",
        null, "customer_city", null, "NeedsClarification", Ambiguous: true,
        ExpectedSource: null),
    new("A06", "Fiyat mi odeme mi gecen ay hangisini raporlayalim",
        null, null, DateRangeKind.Relative, "NeedsClarification", Ambiguous: true,
        ExpectedSource: null),

    new("X01", "Bu yil reklam tiklama basina donusum orani",
        null, null, DateRangeKind.Relative, "Unsupported", Unsupported: true,
        ExpectedSource: null),
    new("X02", "2018 magazalarinin faaliyet kari marji",
        null, null, DateRangeKind.Absolute, "Unsupported", Unsupported: true,
        ExpectedSource: null),
    new("X03", "Gelecek ceyrek stok talep tahmini",
        null, null, DateRangeKind.Relative, "Unsupported", Unsupported: true,
        ExpectedSource: null),
    new("X04", "Musteri yas grubuna gore kampanya ROI",
        null, null, null, "Unsupported", Unsupported: true,
        ExpectedSource: null),
    new("X05", "Web oturumu kaynakli churn risk skoru",
        null, null, null, "Unsupported", Unsupported: true,
        ExpectedSource: null),
    new("X06", "Satici komisyon yuzdesini hesapla",
        null, null, null, "Unsupported", Unsupported: true,
        ExpectedSource: null)
];

file sealed class NullAuditWriter : IDecisionAuditWriter
{
    public void Write(DecisionAuditRecord record)
    {
    }
}
