using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.Nlu;
using Microsoft.Extensions.Options;

namespace CrmAnalytics.Infrastructure.Integrations;

public sealed partial class OllamaStructuredPlanningClient(
    HttpClient httpClient,
    IOptions<OllamaOptions> options,
    OllamaCanonicalContract contract)
    : IOllamaStructuredPlanningClient
{
    private static readonly JsonSerializerOptions TransportOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<OllamaStructuredPlanningResult> PlanAsync(
        OllamaStructuredPlanningRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var schema = contract.CreateSchema(
                request.RequestId, request.ConversationId, request.CandidateConstraints);
            var payload = new OllamaChatRequest(
                options.Value.Model,
                CreateMessages(request),
                Stream: false,
                Think: false,
                options.Value.KeepAlive,
                new OllamaChatOptions(options.Value.Temperature),
                schema);
            using var response = await httpClient.PostAsJsonAsync(
                "/api/chat", payload, TransportOptions, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return Failure("HttpFailure", "OLLAMA_HTTP_FAILURE", stopwatch);
            }

            var envelope = await response.Content.ReadFromJsonAsync<OllamaChatResponse>(
                TransportOptions, cancellationToken);
            var content = envelope?.Message?.Content;
            if (string.IsNullOrWhiteSpace(content))
            {
                return Failure("EmptyResponse", "OLLAMA_EMPTY_RESPONSE", stopwatch,
                    envelope);
            }

            if (content.Length > 65_536 || ContainsSql(content))
            {
                return Failure("UnsafeOutput", "OLLAMA_UNSAFE_OUTPUT", stopwatch,
                    envelope);
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(content, new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 32
                });
            }
            catch (JsonException)
            {
                return Failure("InvalidJson", "OLLAMA_INVALID_JSON", stopwatch,
                    envelope);
            }

            using (document)
            {
                if (!contract.Validate(document.RootElement, request.RequestId,
                        request.ConversationId, request.CandidateConstraints))
                {
                    var category = contract.ClassifyValidationFailure(
                        document.RootElement, request.RequestId, request.ConversationId,
                        request.CandidateConstraints);
                    return Failure("SchemaRejected",
                        $"OLLAMA_SCHEMA_REJECTED_{category.ToUpperInvariant()}", stopwatch,
                        envelope);
                }

                PlanningResult plan;
                try
                {
                    plan = contract.Deserialize(document.RootElement);
                }
                catch (JsonException)
                {
                    return Failure("DeserializeRejected", "OLLAMA_DESERIALIZE_REJECTED",
                        stopwatch, envelope, schemaSucceeded: true);
                }

                return new OllamaStructuredPlanningResult(
                    plan,
                    plan.Outcome.ToString(),
                    "NONE",
                    stopwatch.ElapsedMilliseconds,
                    SchemaValidationSucceeded: true,
                    envelope?.DoneReason,
                    envelope?.PromptEvalCount,
                    envelope?.EvalCount);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failure("Timeout", "OLLAMA_TIMEOUT", stopwatch);
        }
        catch (HttpRequestException)
        {
            return Failure("Unavailable", "OLLAMA_UNAVAILABLE", stopwatch);
        }
        catch (JsonException)
        {
            return Failure("InvalidEnvelope", "OLLAMA_INVALID_ENVELOPE", stopwatch);
        }
    }

    public async Task<OllamaSemanticPlanningResult> PlanSemanticAsync(
        OllamaSemanticPlanningRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var dateKindConstraint = RelativeDateResolver.Resolve(
                TurkishTextNormalizer.Tokenize(request.UserPrompt),
                request.Today)?.Range.Kind;
            var schema = contract.CreateSemanticSchema(dateKindConstraint);
            var payload = new OllamaChatRequest(
                options.Value.Model,
                CreateSemanticMessages(request),
                Stream: false,
                Think: false,
                options.Value.KeepAlive,
                new OllamaChatOptions(options.Value.Temperature, 224, 8192),
                schema);
            using var response = await httpClient.PostAsJsonAsync(
                "/api/chat", payload, TransportOptions, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return SemanticFailure(
                    "HttpFailure",
                    $"OLLAMA_SEMANTIC_HTTP_FAILURE_{(int)response.StatusCode}",
                    stopwatch);
            }

            var envelope = await response.Content.ReadFromJsonAsync<OllamaChatResponse>(
                TransportOptions, cancellationToken);
            var content = envelope?.Message?.Content;
            if (string.IsNullOrWhiteSpace(content))
            {
                return SemanticFailure(
                    "EmptyResponse", "OLLAMA_SEMANTIC_EMPTY_RESPONSE", stopwatch,
                    envelope);
            }
            if (content.Length > 65_536 || ContainsSql(content))
            {
                return SemanticFailure(
                    "UnsafeOutput", "OLLAMA_SEMANTIC_UNSAFE_OUTPUT", stopwatch,
                    envelope);
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(content, new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 32
                });
            }
            catch (JsonException)
            {
                return SemanticFailure(
                    "InvalidJson", "OLLAMA_SEMANTIC_INVALID_JSON", stopwatch,
                    envelope);
            }

            using (document)
            {
                if (!contract.ValidateSemantic(document.RootElement))
                {
                    var category = contract.ClassifySemanticValidationFailure(
                        document.RootElement);
                    return SemanticFailure(
                        "SchemaRejected",
                        $"OLLAMA_SEMANTIC_SCHEMA_REJECTED_{category.ToUpperInvariant()}",
                        stopwatch, envelope);
                }

                ExtractedSemanticPlanningResult plan;
                try
                {
                    plan = contract.DeserializeSemantic(document.RootElement);
                }
                catch (JsonException)
                {
                    return SemanticFailure(
                        "DeserializeRejected",
                        "OLLAMA_SEMANTIC_DESERIALIZE_REJECTED",
                        stopwatch, envelope, schemaSucceeded: true);
                }

                return new OllamaSemanticPlanningResult(
                    plan,
                    plan.Outcome.ToString(),
                    "NONE",
                    stopwatch.ElapsedMilliseconds,
                    true,
                    envelope?.DoneReason,
                    envelope?.PromptEvalCount,
                    envelope?.EvalCount);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return SemanticFailure(
                "Timeout", "OLLAMA_SEMANTIC_TIMEOUT", stopwatch);
        }
        catch (HttpRequestException)
        {
            return SemanticFailure(
                "Unavailable", "OLLAMA_SEMANTIC_UNAVAILABLE", stopwatch);
        }
        catch (JsonException)
        {
            return SemanticFailure(
                "InvalidEnvelope", "OLLAMA_SEMANTIC_INVALID_ENVELOPE", stopwatch);
        }
    }

    public async Task<SemanticGapPlanningResult> ResolveGapsAsync(
        SemanticGapPlanningRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var payload = new OllamaChatRequest(
                options.Value.Model,
                CreateGapMessages(request),
                Stream: false,
                Think: false,
                options.Value.KeepAlive,
                new OllamaChatOptions(options.Value.Temperature),
                CreateGapSchema(request));
            using var response = await httpClient.PostAsJsonAsync(
                "/api/chat", payload, TransportOptions, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return SemanticGapPlanningResult.Failure(
                    "HttpFailure", "OLLAMA_GAP_HTTP_FAILURE", stopwatch.ElapsedMilliseconds);
            }

            var envelope = await response.Content.ReadFromJsonAsync<OllamaChatResponse>(
                TransportOptions, cancellationToken);
            var content = envelope?.Message?.Content;
            if (string.IsNullOrWhiteSpace(content) || content.Length > 16_384
                || ContainsSql(content))
            {
                return SemanticGapPlanningResult.Failure(
                    "UnsafeOutput", "OLLAMA_GAP_UNSAFE_OUTPUT", stopwatch.ElapsedMilliseconds);
            }

            using var document = JsonDocument.Parse(content, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 16
            });
            var selection = ParseGapSelection(document.RootElement, request);
            if (selection is null)
            {
                return SemanticGapPlanningResult.Failure(
                    "SchemaRejected", "OLLAMA_GAP_SCHEMA_REJECTED",
                    stopwatch.ElapsedMilliseconds);
            }

            return new SemanticGapPlanningResult(
                selection,
                selection.Outcome.ToString(),
                "NONE",
                stopwatch.ElapsedMilliseconds,
                true,
                envelope?.PromptEvalCount,
                envelope?.EvalCount);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return SemanticGapPlanningResult.Failure(
                "Timeout", "OLLAMA_GAP_TIMEOUT", stopwatch.ElapsedMilliseconds);
        }
        catch (JsonException)
        {
            return SemanticGapPlanningResult.Failure(
                "InvalidJson", "OLLAMA_GAP_INVALID_JSON",
                stopwatch.ElapsedMilliseconds);
        }
        catch (HttpRequestException exception)
        {
            return SemanticGapPlanningResult.Failure(
                exception.GetType().Name, "OLLAMA_GAP_INVALID_RESPONSE",
                stopwatch.ElapsedMilliseconds);
        }
    }

    private IReadOnlyList<OllamaChatMessage> CreateMessages(
        OllamaStructuredPlanningRequest request)
    {
        var system = string.Join('\n',
            "Yalniz JSON uret; Markdown, code fence veya aciklama uretme.",
            "SQL uretme; tablo, view veya kolon secme ya da adlandirma.",
            "Gorevin yalniz intent, source, semantic metric/dimension/filter key, filter value, tarih ve limit secmektir.",
            "Yalniz format JSON Schema'sinda ve runtime katalogda bulunan semantic key'leri kullan.",
            "Catalog'da karsiligi olmayan business concept icin en yakin semantic key'i secme ve metric substitution yapma.",
            "Kullanicinin acikca istedigi her onemli business concept ya desteklenen bir semantic key'e resolve edilmeli ya unresolvedConcepts icinde semantic slot kind'i ile isaretlenmelidir.",
            "Bir business concept unresolved ise outcome accepted olamaz; confidence bu kurali degistiremez.",
            "Eksik veya belirsiz bilgi kullanicidan alinabilecek turdeyse needs_clarification ve backend'in soracagi semantic slotu clarification.kind ile belirt.",
            "Acikca istenen business metric veya dimension katalog tarafindan desteklenmiyorsa unsupported kullan.",
            "accepted icin canonicalRequest zorunlu, unresolvedConcepts bos ve clarification null olmalidir.",
            "needs_clarification icin canonicalRequest null ve clarification dolu olmalidir.",
            "unsupported icin canonicalRequest null, unresolvedConcepts en az bir typed semantic slot icermeli ve clarification null olmalidir.",
            "Metric, dimension ve filter secimlerinde katalog compatibility metadata'sina uy.",
            "Tarih araligi yalniz dateRange alanina yazilir; kullanici zaman bazinda gruplama istemediyse tarih dimension'i ekleme.",
            "Acik takvim tarihleri absolute, gorece zaman sozleri relative dateRange olur; tarih gerektirmeyen metric icin not_applicable kullan.",
            "Kullanicinin soylemedigi filtreyi uydurma. Yetki veya data-scope karari verme.",
            "SQL, physical object veya authorization karari verme.",
            "Unresolved concept icine kullanici promptunu veya serbest metni kopyalama; yalniz schema'daki typed kind degerini kullan.",
            $"Bugun: {request.Today:yyyy-MM-dd}. Relative tarihlerin from/to degerlerini bu tarihe gore cozumle.",
            "Planning alanlari: outcome, canonicalRequest, unresolvedConcepts, clarification.",
            "Accepted canonical alanlari: requestId, conversationId, previousRequestId, source, intent, metrics, dimensions, filters, dateRange, grain, limit, orderBy, orderDirection, scenarioKey, confidence, unresolvedTerms.",
            "Accepted canonical icinde scenarioKey null ve unresolvedTerms bos olmalidir. Kimlikleri format semasindaki const degerlerden aynen kullan.",
            "Guvenli katalog (fiziksel sema bilgisi icermez):",
            contract.CreateCatalogPrompt(request.CandidateConstraints),
            "FINAL SEMANTIC COVERAGE CHECK:",
            "accepted secmeden once kullanicinin acikca istedigi her business metric, dimension, filter, date ve source kavramini ayri ayri kontrol et.",
            "Bir katalog kaydini yalniz name, description veya conceptualAliases ayni business anlamini acikca ifade ediyorsa eslestir; ayni domain, unit, value type veya aggregation yeterli degildir.",
            "Tek bir unresolved kavramin yerine ilgili gorunen bir veya birden cok supported key koyma.",
            "Acikca istenen metric veya dimension icin semantic olarak esdeger katalog kaydi yoksa unsupported sec; kullanici kavrami belirtmemis veya birden cok destekli yorum mumkunse needs_clarification sec.",
            "Herhangi bir unresolvedConcept varsa accepted secme.");

        var messages = new List<OllamaChatMessage>
        {
            new("system", system)
        };
        if (request.OriginalPrompt is not null
            || request.ClarificationQuestion is not null
            || request.ClarificationAnswer is not null)
        {
            messages.Add(new("user",
                "ORIGINAL_USER_REQUEST\n" + (request.OriginalPrompt ?? request.UserPrompt)));
            messages.Add(new("user",
                "CLARIFICATION_QUESTION_MEANING\n" +
                (request.ClarificationQuestion ?? "Belirtilmedi")));
            messages.Add(new("user",
                "SINGLE_CLARIFICATION_ANSWER\n" +
                (request.ClarificationAnswer ?? request.UserPrompt)));
        }
        else
        {
            messages.Add(new("user", request.UserPrompt));
        }

        return messages;
    }

    private IReadOnlyList<OllamaChatMessage> CreateSemanticMessages(
        OllamaSemanticPlanningRequest request)
    {
        var system = string.Join('\n',
            "Yalniz strict JSON uret; Markdown, code fence veya aciklama uretme.",
            "Gorevin yalniz kullanicinin semantic intent'ini cikarmaktir.",
            "SQL, tablo, view, kolon, source, join, authorization, data scope veya execution policy secme ve uretme.",
            "Yalniz katalogdaki semantic key'leri ve schema'daki semantic operasyonlari kullan.",
            "Bir count/quantity metric'in saydigi business entity yalniz kullanicinin metric ifadesinden belirlenir; group_by dimension bu entity'yi degistiremez.",
            "Group_by dimension'i metric secimi icin kanit olarak kullanma; metric label/description sayilan entity ile ayni degilse o metric'i secme.",
            "Catalog'da semantic olarak esdegeri olmayan kavram icin yakin bir key secme veya metric substitution yapma.",
            "Acikca istenen desteklenmeyen kavram icin unsupported; eksik veya birden cok makul yorum icin needs_clarification kullan.",
            "accepted icin semanticIntent zorunlu, unresolvedConcepts bos ve clarification null olmalidir.",
            "needs_clarification icin semanticIntent null ve clarification dolu olmalidir.",
            "unsupported icin semanticIntent null, unresolvedConcepts en az bir typed slot icermeli ve clarification null olmalidir.",
            "Filter degerlerini values icinde ham semantic literal olarak tut; SQL veya escape uretme.",
            "Filter yalniz kullanici acik bir literal degerle kisit koyduysa uretilir; group_by/kirilim/bazinda ifadeleri filter degildir.",
            "Acik bir filter literal'i yoksa filters kesinlikle bos array olmalidir.",
            "Her filter literal kullanici metninde aynen bulunan somut bir dimension uye degeri olmalidir; literal uydurma, cevirme veya yeniden ifade etme.",
            "Metric, dimension, date veya ranking anlamini eslestirmek icin kullanilan kelime araligini filter literal olarak tekrar kullanma.",
            "Dimension label'i, description'i veya conceptual alias'i o dimension'in uye degeri degildir ve filter literal olamaz.",
            "Relative tarih icin yalniz semantic token ve gerekiyorsa count uret; gercek from/to hesabini yapma.",
            "relativeExpression last_n_days/last_n_weeks/last_n_months/last_n_years ise count kullanicinin belirttigi pozitif tam sayi olmak zorundadir.",
            "Diger relativeExpression degerlerinde count null olmalidir; relative tarihte from ve to her zaman null olmalidir.",
            "Kullanicinin relative/goreli tarih ifadesini absolute tarihe cevirme; relative wording her zaman relativeExpression ve gerekiyorsa count ile tasinir.",
            "Absolute date yalniz kullanici acik takvim tarihi veya araligi verdiyse kullanilir.",
            "Tarih yalniz filter araligiysa grain none olmalidir; grain ancak kullanici acikca zaman ekseninde trend/group_by istediyse ve groupBy bir zaman dimension'i iceriyorsa kullanilir.",
            "Absolute tarih araliginda kullanicinin verdigi from/to degerlerini yyyy-MM-dd biciminde aktar.",
            "Kullanici tarih belirtmediyse date.kind unspecified kullan; tarih gereksinimini backend belirler.",
            "Kullanici top/ranking istemediyse ranking null olsun; istemisse topN, semantic orderBy dimension ve direction uret.",
            "Model-facing guvenli semantic katalog:",
            contract.CreateSemanticCatalogPrompt(),
            "Her katalog girdisi yalniz semanticKey, label, description, conceptualAliases ve allowedSemanticOperations icerir.",
            "FINAL COVERAGE CHECK: Acikca istenen her metric, group_by, filter, date ve ranking kavramini kontrol et; herhangi biri cozulmediyse accepted secme.");

        var messages = new List<OllamaChatMessage> { new("system", system) };
        if (request.OriginalPrompt is not null
            || request.ClarificationQuestion is not null
            || request.ClarificationAnswer is not null)
        {
            messages.Add(new("user",
                "ORIGINAL_USER_REQUEST\n" + (request.OriginalPrompt ?? request.UserPrompt)));
            messages.Add(new("user",
                "CLARIFICATION_QUESTION_MEANING\n" +
                (request.ClarificationQuestion ?? "Belirtilmedi")));
            messages.Add(new("user",
                "SINGLE_CLARIFICATION_ANSWER\n" +
                (request.ClarificationAnswer ?? request.UserPrompt)));
        }
        else
        {
            messages.Add(new("user", request.UserPrompt));
        }
        return messages;
    }

    private IReadOnlyList<OllamaChatMessage> CreateGapMessages(
        SemanticGapPlanningRequest request)
    {
        var fixedMetric = request.State.ResolvedMetric ?? "none";
        var fixedDimensions = string.Join(',', request.State.ResolvedDimensions);
        var fixedDate = request.State.ResolvedDate is null
            ? "none"
            : JsonSerializer.Serialize(request.State.ResolvedDate, TransportOptions);
        var system = string.Join('\n',
            "Yalniz JSON uret; SQL, Markdown veya aciklama uretme.",
            "Gorevin tum talebi yeniden planlamak degil, yalniz belirtilen belirsiz semantic slotlari secmektir.",
            "Resolved alanlar immutable backend constraint'tir; bunlari cikarma, degistirme veya yeniden yorumlama.",
            $"Resolved metric: {fixedMetric}",
            $"Resolved dimensions: {fixedDimensions}",
            $"Resolved date: {fixedDate}",
            $"Unresolved slots: {string.Join(',', request.State.QwenResolvableSlots.Select(slot => slot.Kind))}",
            "Yalniz allowed candidate key'lerden sec. Uygun secim guvenle yapilamiyorsa needs_clarification sec.",
            "Guvenli aday katalog:",
            contract.CreateCatalogPrompt(request.CandidateConstraints));
        return
        [
            new OllamaChatMessage("system", system),
            new OllamaChatMessage("user", request.UserPrompt)
        ];
    }

    private static JsonObject CreateGapSchema(SemanticGapPlanningRequest request)
    {
        var metricAmbiguous = request.State.QwenResolvableSlots.Any(slot =>
            slot.Kind == UnresolvedConceptKind.Metric);
        var dimensionAmbiguous = request.State.QwenResolvableSlots.Any(slot =>
            slot.Kind is UnresolvedConceptKind.Dimension
                or UnresolvedConceptKind.Filter);
        var metricSchema = metricAmbiguous
            ? new JsonObject
            {
                ["type"] = "string",
                ["enum"] = new JsonArray(request.CandidateConstraints.MetricKeys
                    .Select(value => JsonValue.Create(value)).ToArray())
            }
            : new JsonObject { ["type"] = "null", ["const"] = null };
        var dimensionSchema = new JsonObject
        {
            ["type"] = "array",
            ["minItems"] = dimensionAmbiguous ? 1 : 0,
            ["maxItems"] = dimensionAmbiguous ? 1 : 0,
            ["uniqueItems"] = true,
            ["items"] = new JsonObject
            {
                ["type"] = "string",
                ["enum"] = new JsonArray(request.CandidateConstraints.DimensionKeys
                    .Select(value => JsonValue.Create(value)).ToArray())
            }
        };
        return new JsonObject
        {
            ["title"] = "Semantic Gap Resolution",
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new JsonArray(
                "outcome", "metricKey", "dimensionKeys", "clarification"),
            ["properties"] = new JsonObject
            {
                ["outcome"] = new JsonObject
                {
                    ["type"] = "string",
                    ["enum"] = new JsonArray("resolved", "needs_clarification")
                },
                ["metricKey"] = metricSchema,
                ["dimensionKeys"] = dimensionSchema,
                ["clarification"] = new JsonObject
                {
                    ["anyOf"] = new JsonArray(
                        new JsonObject { ["type"] = "null" },
                        new JsonObject
                        {
                            ["type"] = "object",
                            ["additionalProperties"] = false,
                            ["required"] = new JsonArray("kind"),
                            ["properties"] = new JsonObject
                            {
                                ["kind"] = new JsonObject
                                {
                                    ["type"] = "string",
                                    ["enum"] = new JsonArray(request.State.QwenResolvableSlots
                                        .Select(slot => slot.Kind.ToString().ToLowerInvariant())
                                        .Distinct(StringComparer.Ordinal)
                                        .Select(value => JsonValue.Create(value)).ToArray())
                                }
                            }
                        })
                }
            }
        };
    }

    private static SemanticGapSelection? ParseGapSelection(
        JsonElement root,
        SemanticGapPlanningRequest request)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var properties = root.EnumerateObject().Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);
        if (!properties.SetEquals(
                ["outcome", "metricKey", "dimensionKeys", "clarification"])
            || root.GetProperty("outcome").ValueKind != JsonValueKind.String
            || root.GetProperty("dimensionKeys").ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var outcome = root.GetProperty("outcome").GetString();
        var clarificationElement = root.GetProperty("clarification");
        if (outcome == "needs_clarification")
        {
            if (clarificationElement.ValueKind != JsonValueKind.Object
                || !clarificationElement.TryGetProperty("kind", out var kindElement)
                || kindElement.ValueKind != JsonValueKind.String
                || !TryParseGapKind(kindElement.GetString(), out var clarificationKind)
                || !request.State.QwenResolvableSlots.Any(slot =>
                    slot.Kind == clarificationKind))
            {
                return null;
            }
            return new SemanticGapSelection(SemanticGapOutcome.NeedsClarification,
                null, [], new PlanningClarification(clarificationKind));
        }

        if (outcome != "resolved" || clarificationElement.ValueKind != JsonValueKind.Null)
        {
            return null;
        }

        var metricElement = root.GetProperty("metricKey");
        var metricAmbiguous = request.State.QwenResolvableSlots.Any(slot =>
            slot.Kind == UnresolvedConceptKind.Metric);
        var metric = metricElement.ValueKind == JsonValueKind.Null
            ? null : metricElement.ValueKind == JsonValueKind.String
                ? metricElement.GetString() : null;
        if (metricAmbiguous != (metric is not null)
            || metric is not null && !request.CandidateConstraints.MetricKeys.Contains(
                metric, StringComparer.Ordinal))
        {
            return null;
        }

        var dimensions = root.GetProperty("dimensionKeys").EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()!).ToArray();
        var dimensionAmbiguous = request.State.QwenResolvableSlots.Any(slot =>
            slot.Kind is UnresolvedConceptKind.Dimension
                or UnresolvedConceptKind.Filter);
        if (dimensions.Length != root.GetProperty("dimensionKeys").GetArrayLength()
            || dimensions.Length > 1
            || dimensionAmbiguous && dimensions.Length == 0
            || !dimensionAmbiguous && dimensions.Length > 0
            || dimensions.Distinct(StringComparer.Ordinal).Count() != dimensions.Length
            || dimensions.Any(key => !request.CandidateConstraints.DimensionKeys.Contains(
                key, StringComparer.Ordinal)))
        {
            return null;
        }

        return new SemanticGapSelection(
            SemanticGapOutcome.Resolved, metric, dimensions, null);
    }

    private static bool TryParseGapKind(
        string? value,
        out UnresolvedConceptKind kind)
    {
        kind = value switch
        {
            "metric" => UnresolvedConceptKind.Metric,
            "dimension" => UnresolvedConceptKind.Dimension,
            "filter" => UnresolvedConceptKind.Filter,
            "date" => UnresolvedConceptKind.Date,
            "source" => UnresolvedConceptKind.Source,
            _ => (UnresolvedConceptKind)(-1)
        };
        return Enum.IsDefined(kind);
    }

    private static OllamaStructuredPlanningResult Failure(
        string outcome,
        string reasonCode,
        Stopwatch stopwatch,
        OllamaChatResponse? response = null,
        bool schemaSucceeded = false) => new(
            null,
            outcome,
            reasonCode,
            stopwatch.ElapsedMilliseconds,
            schemaSucceeded,
            response?.DoneReason,
            response?.PromptEvalCount,
            response?.EvalCount);

    private static OllamaSemanticPlanningResult SemanticFailure(
        string outcome,
        string reasonCode,
        Stopwatch stopwatch,
        OllamaChatResponse? response = null,
        bool schemaSucceeded = false) => new(
            null,
            outcome,
            reasonCode,
            stopwatch.ElapsedMilliseconds,
            schemaSucceeded,
            response?.DoneReason,
            response?.PromptEvalCount,
            response?.EvalCount);

    private static bool ContainsSql(string content) => SqlKeywordRegex().IsMatch(content)
        || SqlCommentRegex().IsMatch(content);

    [GeneratedRegex(@"(?i)\b(SELECT|INSERT|UPDATE|DELETE|DROP|MERGE|CREATE|ALTER)\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex SqlKeywordRegex();

    [GeneratedRegex(@"(--|/\*|\*/)", RegexOptions.CultureInvariant)]
    private static partial Regex SqlCommentRegex();

    internal sealed record OllamaChatRequest(
        string Model,
        IReadOnlyList<OllamaChatMessage> Messages,
        bool Stream,
        bool Think,
        [property: JsonPropertyName("keep_alive")] string KeepAlive,
        OllamaChatOptions Options,
        JsonObject Format);

    internal sealed record OllamaChatMessage(string Role, string Content);

    internal sealed record OllamaChatOptions(
        double Temperature,
        int? NumPredict = null,
        int? NumCtx = null);

    internal sealed record OllamaChatResponse(
        OllamaChatResponseMessage? Message,
        [property: JsonPropertyName("done_reason")] string? DoneReason,
        [property: JsonPropertyName("prompt_eval_count")] int? PromptEvalCount,
        [property: JsonPropertyName("eval_count")] int? EvalCount);

    internal sealed record OllamaChatResponseMessage(string? Content);
}
