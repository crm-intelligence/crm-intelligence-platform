using Crm.Analytics.Sql.Catalog;
using Crm.Analytics.Sql.Contracts;

namespace Crm.Analytics.Sql.Nlu;

/// <summary>
/// Serbest metni <b>yalnizca Metric Catalog'daki terimlere</b> bakarak Canonical Request'e cevirir.
/// </summary>
/// <remarks>
/// <para>
/// <b>Neden deterministik:</b> ayristirici bir dil modeli olsaydi, "onceki tum kurallari yok say"
/// turu bir girdi onun davranisini degistirmeye calisabilirdi. Bu uygulama metinde yalnizca
/// katalog alias'larini arar; tanimadigi hicbir kelimeye anlam atamaz. Prompt injection
/// denemeleri hicbir terime baglanmadigi icin guven skorunu dusurur ve netlestirmeye gider.
/// </para>
/// <para>
/// <b>Uretmedigi seyler:</b> filtre kosullari. Bir filtre kurmak icin boyut <i>degerlerini</i>
/// bilmek gerekir ("SP" bir eyalet kodu mu, yoksa anlasilmayan bir kelime mi?). Katalog bugun
/// deger sozlugu tasimiyor; uydurma deger eslemesi yapmak yerine filtre uretimi kapsam disinda
/// birakilmistir. Filtreler yapilandirilmis olarak (UI secimi veya takip sorusu) gelir.
/// </para>
/// <para>
/// <b>Cekim eki toleransi:</b> "eyalete gore" yazan kullanici ile katalogdaki "eyalet" alias'i
/// eslesmelidir. Kok dort karakter veya daha uzunsa on ek eslesmesi yeterli sayilir; daha kisa
/// koklerde en fazla iki karakter fazlaya izin verilir. Bu bir morfolojik cozumleme degildir —
/// dogrulugu katalog alias'larinin ayirt edici secilmesine baglidir.
/// </para>
/// </remarks>
public sealed class CatalogTermRequestParser : IRequestParser
{
    /// <summary>
    /// Katalog terimi olmasi beklenmeyen kelimeler. Bunlar cozumlenemeyen terim sayilmaz,
    /// aksi halde her nazik cumle ("bana ... gosterir misin") guven skorunu dusururdu.
    /// </summary>
    private static readonly HashSet<string> FillerWords = new(StringComparer.Ordinal)
    {
        "bana", "bize", "goster", "gosterir", "gosterebilir", "misin", "musun", "lutfen",
        "ne", "nedir", "kadar", "icin", "gore", "ve", "veya", "ile", "bu", "su", "o", "olan",
        "tum", "tumu", "hepsi", "bazinda", "bazli", "rapor", "raporu", "analiz", "analizi",
        "istiyorum", "isterim", "ver", "verir", "getir", "nasil", "kac", "olarak",
        "toplam", "toplami", "deger", "degeri", "sonuc", "sonucu", "bilgi", "bilgisi",
        "tablo", "tablosu", "grafik", "grafigi", "ozet", "ozeti", "detay", "detayi",
        "arasi", "arasinda", "tarihinden", "tarihine", "tarihleri", "tarihinde", "itibariyle",
        "yalnizca", "sadece", "da", "de", "ki", "mi", "mu", "en", "bir", "her"
    };

    private readonly MetricCatalogDocument catalog;
    private readonly List<TermEntry> terms;

    public CatalogTermRequestParser(MetricCatalogDocument catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        this.catalog = catalog;
        terms = BuildTermIndex(catalog);
    }

    public RequestParseOutcome Parse(RequestParseInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (string.IsNullOrWhiteSpace(input.Prompt))
        {
            return RequestParseOutcome.Failure(ReasonCode.CL001, "Talep metni bos.");
        }

        var tokens = TurkishTextNormalizer.Tokenize(input.Prompt);

        if (tokens.Count == 0)
        {
            return RequestParseOutcome.Failure(ReasonCode.CL001, "Talep metni cozumlenebilir kelime icermiyor.");
        }

        var consumed = new bool[tokens.Count];

        // Sira onemli: tarih ifadesi once tuketilir. "bu ay" ifadesindeki "ay" kelimesi
        // aksi halde zaman kirilimi ("ay bazinda") olarak da okunabilirdi.
        var dateMatch = RelativeDateResolver.Resolve(tokens, input.Today);
        MarkConsumed(consumed, dateMatch?.TokenStart ?? 0, dateMatch?.TokenCount ?? 0);

        var grain = ResolveGrain(tokens, consumed);
        var (metrics, dimensions) = ResolveTerms(tokens, consumed);
        var intent = metrics.Count == 0 && dimensions.Count > 0
            && dimensions.All(key => catalog.FindDimension(key)?.Groupable == false)
                ? RequestIntent.List
                : ResolveIntent(tokens, consumed, grain, dimensions.Count);

        if (metrics.Count == 0 && dimensions.Count == 0)
        {
            return RequestParseOutcome.Failure(
                ReasonCode.CL001,
                "Talepte katalogda tanimli hicbir metrik veya boyut bulunamadi.",
                CollectUnresolved(tokens, consumed));
        }

        if (dateMatch is null && RequiresDateRange(metrics, dimensions))
        {
            // Varsayilan aralik atanmaz: tarihsiz talep tum donemi tarar, bu hem tarih
            // butcesini etkisiz kilar hem de kullanicinin kastetmedigi bir donemi sessizce
            // raporlamak olurdu. Zaman boyutu tasimayan kaynaklarda ise aralik istemek
            // cikmaz sokaktir, bu yuzden kural kaynaga bagli.
            return RequestParseOutcome.Failure(
                ReasonCode.CL002,
                "Talepte tarih araligi bulunamadi; secilen veri alani tarih araligi gerektiriyor.",
                CollectUnresolved(tokens, consumed));
        }

        var timeDimensionFailure = EnsureTimeDimensionForGrain(grain, metrics, dimensions);

        if (timeDimensionFailure is not null)
        {
            return timeDimensionFailure;
        }

        var unresolved = CollectUnresolved(tokens, consumed);

        return RequestParseOutcome.Success(new CanonicalRequest
        {
            RequestId = input.RequestId,
            ConversationId = input.ConversationId,
            PreviousRequestId = input.PreviousRequestId,
            Intent = intent,
            Metrics = metrics,
            Dimensions = dimensions,
            Filters = [],
            DateRange = dateMatch?.Range ?? DateRangeSpec.NotApplicable,
            Grain = grain,
            Confidence = ComputeConfidence(tokens, consumed),
            UnresolvedTerms = unresolved
        });
    }

    /// <summary>
    /// Takip sorusunu <b>delta</b> olarak cozumler: yalnizca metinde gecen alanlar doldurulur,
    /// gerisi "degismedi" olarak birakilir.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Neden ayri bir mod: "satis adedi yerine siparis sayisi" talebinde tarih araligi yoktur.
    /// Tam talep modunda bu CL002 ile reddedilirdi; oysa kullanici tarihi degistirmedigini,
    /// oncekinin gecerli oldugunu kastediyor.
    /// </para>
    /// <para>
    /// Delta bos donmez: hicbir alan taninmadiysa netlestirme istenir. Bos bir delta onceki
    /// raporu aynen tekrar uretir ve kullaniciya "istegin uygulandi" izlenimi verirdi.
    /// </para>
    /// </remarks>
    public RequestDeltaOutcome ParseRevision(RequestParseInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var tokens = TurkishTextNormalizer.Tokenize(input.Prompt ?? string.Empty);

        if (tokens.Count == 0)
        {
            return RequestDeltaOutcome.Failure(ReasonCode.CL001, "Revizyon metni cozumlenebilir kelime icermiyor.");
        }

        var consumed = new bool[tokens.Count];

        var dateMatch = RelativeDateResolver.Resolve(tokens, input.Today);
        MarkConsumed(consumed, dateMatch?.TokenStart ?? 0, dateMatch?.TokenCount ?? 0);

        var grain = ResolveGrain(tokens, consumed);
        var (metrics, dimensions) = ResolveTerms(tokens, consumed);
        var limit = ResolveLimit(tokens, consumed);
        var direction = ResolveDirection(tokens, consumed);
        var sortRequested = ConsumeMarker(tokens, consumed, "sirala", "siralama");
        var filterRequested = ConsumeMarker(
            tokens, consumed, "filtre", "filtrele", "olsun", "ekle", "degistir");

        string? orderBy = null;
        IReadOnlyList<RequestFilter>? filtersToAdd = null;
        if (sortRequested && filterRequested)
        {
            return RequestDeltaOutcome.Failure(
                ReasonCode.CL001,
                "Tek revizyonda ayni alan hem filtre hem siralama olarak yorumlanamaz.");
        }

        if ((sortRequested || filterRequested) && dimensions.Count != 1)
        {
            return RequestDeltaOutcome.Failure(
                ReasonCode.CL001,
                "Siralama veya filtre revizyonu tam olarak bir catalog alani belirtmelidir.",
                CollectUnresolved(tokens, consumed),
                ComputeConfidence(tokens, consumed));
        }

        if (sortRequested)
        {
            orderBy = dimensions[0];
            dimensions.Clear();
        }

        if (filterRequested)
        {
            var field = dimensions[0];
            var definition = catalog.FindDimension(field)!;
            if (!definition.Filterable)
            {
                return RequestDeltaOutcome.Failure(
                    ReasonCode.CL001,
                    "Istenen alan icin filter capability'si etkin degil.");
            }

            var valueIndexes = Enumerable.Range(0, tokens.Count)
                .Where(index => !consumed[index]
                    && !FillerWords.Contains(tokens[index].Normalized))
                .ToArray();
            if (valueIndexes.Length != 1)
            {
                return RequestDeltaOutcome.Failure(
                    ReasonCode.CL001,
                    "Filtre revizyonu tek ve acik bir deger belirtmelidir.",
                    CollectUnresolved(tokens, consumed),
                    ComputeConfidence(tokens, consumed));
            }

            var valueIndex = valueIndexes[0];
            consumed[valueIndex] = true;
            filtersToAdd =
            [
                new RequestFilter
                {
                    Field = field,
                    Op = FilterOperator.Eq,
                    Values =
                    [
                        new FilterLiteral(
                            MapFilterKind(definition.ValueType),
                            tokens[valueIndex].Raw)
                    ]
                }
            ];
            dimensions.Clear();
        }

        var unresolved = CollectUnresolved(tokens, consumed);
        var confidence = ComputeConfidence(tokens, consumed);

        if (metrics.Count == 0 && dimensions.Count == 0 && dateMatch is null
            && grain == TimeGrain.None && limit is null && orderBy is null
            && filtersToAdd is null && direction is null)
        {
            return RequestDeltaOutcome.Failure(
                ReasonCode.CL001,
                "Revizyon talebinde degistirilecek hicbir alan taninmadi.",
                unresolved,
                confidence);
        }

        // Zaman kirilimi degistiyse kirilim listesi de guncellenmelidir; aksi halde grain
        // uygulanacak bir boyut bulamaz.
        if (grain != TimeGrain.None && dimensions.Count == 0)
        {
            var failure = EnsureTimeDimensionForGrain(grain, metrics, dimensions);

            if (failure is not null)
            {
                return RequestDeltaOutcome.Failure(
                    failure.ReasonCode, failure.Detail ?? string.Empty, unresolved, confidence);
            }
        }

        return RequestDeltaOutcome.Success(
            new CanonicalRequestDelta
            {
                Metrics = metrics.Count > 0 ? metrics : null,
                Dimensions = dimensions.Count > 0 ? dimensions : null,
                FiltersToAdd = filtersToAdd,
                DateRange = dateMatch?.Range,
                Grain = grain == TimeGrain.None ? null : grain,
                Limit = limit,
                OrderBy = orderBy,
                OrderDirection = direction
            },
            unresolved,
            confidence);
    }

    private static int? ResolveLimit(IReadOnlyList<PromptToken> tokens, bool[] consumed)
    {
        for (var index = 0; index + 1 < tokens.Count; index++)
        {
            if (consumed[index] || consumed[index + 1]
                || tokens[index].Normalized is not ("ilk" or "top")
                || !int.TryParse(tokens[index + 1].Normalized, out var limit)
                || limit <= 0)
            {
                continue;
            }

            MarkConsumed(consumed, index, 2);
            return limit;
        }

        return null;
    }

    private static SortDirection? ResolveDirection(
        IReadOnlyList<PromptToken> tokens,
        bool[] consumed)
    {
        for (var index = 0; index < tokens.Count; index++)
        {
            if (consumed[index])
            {
                continue;
            }

            var direction = tokens[index].Normalized switch
            {
                "artan" => SortDirection.Asc,
                "azalan" => SortDirection.Desc,
                _ => (SortDirection?)null
            };
            if (direction is not null)
            {
                consumed[index] = true;
                return direction;
            }
        }

        return null;
    }

    private static bool ConsumeMarker(
        IReadOnlyList<PromptToken> tokens,
        bool[] consumed,
        params string[] markers)
    {
        var found = false;
        for (var index = 0; index < tokens.Count; index++)
        {
            if (!consumed[index]
                && markers.Contains(tokens[index].Normalized, StringComparer.Ordinal))
            {
                consumed[index] = true;
                found = true;
            }
        }

        return found;
    }

    private static FilterValueKind MapFilterKind(string valueType) => valueType switch
    {
        "integer" => FilterValueKind.Integer,
        "decimal" => FilterValueKind.Decimal,
        "boolean" => FilterValueKind.Boolean,
        "date" => FilterValueKind.Date,
        _ => FilterValueKind.Text
    };

    /// <summary>
    /// Zaman kirilimi istendiginde GROUP BY'a girecek zaman boyutunu ekler.
    /// </summary>
    /// <remarks>
    /// "Aylik satis trendi" talebinde kullanici zaman boyutunu adiyla yazmaz. Kirilim
    /// olmadan grain'in hicbir etkisi olmaz ve talep sessizce tek satir donerdi.
    /// </remarks>
    private RequestParseOutcome? EnsureTimeDimensionForGrain(
        TimeGrain grain,
        IReadOnlyList<string> metrics,
        List<string> dimensions)
    {
        if (grain == TimeGrain.None || dimensions.Any(IsTimeDimension))
        {
            return null;
        }

        // Zaman boyutu metrigin kaynagiyla ayni objede olmalidir; farkli kaynak birlestirme
        // gerektirir ve maxJoins=0 nedeniyle reddedilirdi.
        var source = metrics
            .Select(catalog.FindMetric)
            .FirstOrDefault(metric => metric is not null)?.Source;

        var timeDimension = catalog.Dimensions
            .FirstOrDefault(pair => pair.Value.IsTimeDimension
                && (source is null || pair.Value.Source.Equals(source, StringComparison.OrdinalIgnoreCase)));

        if (timeDimension.Key is null)
        {
            return RequestParseOutcome.Failure(
                ReasonCode.CL002,
                source is null
                    ? "Katalogda zaman boyutu tanimli degil."
                    : $"'{source}' kaynaginda zaman kirilimi yapilabilecek bir boyut tanimli degil.");
        }

        dimensions.Add(timeDimension.Key);
        return null;
    }

    private bool IsTimeDimension(string key) => catalog.FindDimension(key)?.IsTimeDimension == true;

    /// <summary>
    /// Talebin okudugu kaynakta zaman boyutu var mi. Varsa tarih araligi zorunludur;
    /// yoksa aralik uygulanamaz ve istenmemesi dogrudur.
    /// </summary>
    private bool RequiresDateRange(IReadOnlyList<string> metrics, IReadOnlyList<string> dimensions)
    {
        var source = metrics.Select(catalog.FindMetric).FirstOrDefault(metric => metric is not null)?.Source
            ?? dimensions.Select(catalog.FindDimension).FirstOrDefault(dimension => dimension is not null)?.Source;

        return source is not null && catalog.Dimensions.Values.Any(dimension =>
            dimension.IsTimeDimension && dimension.Source.Equals(source, StringComparison.OrdinalIgnoreCase));
    }

    private (List<string> Metrics, List<string> Dimensions) ResolveTerms(
        IReadOnlyList<PromptToken> tokens,
        bool[] consumed)
    {
        var found = new List<(string Key, bool IsMetric, int Position)>();

        // Uzun alias'lar once denenir: "satis tutari" ifadesi, "satis" tek kelimesine
        // baglanirsa daha ozgul metrik kaybedilirdi.
        foreach (var term in terms)
        {
            if (found.Any(entry => entry.Key == term.Key))
            {
                continue;
            }

            var start = FindPhrase(tokens, consumed, term.Words);

            if (start is null)
            {
                continue;
            }

            MarkConsumed(consumed, start.Value, term.Words.Count);
            found.Add((term.Key, term.IsMetric, start.Value));
        }

        // Sonuc METINDEKI SIRAYA gore dizilir. Eslestirme sirasi alias uzunluguna baglidir;
        // o sirayi korumak, "siparis sayisi ve satis tutari" diyen kullaniciya kolonlari ters
        // sirada gostermek olurdu. Kolon sirasi rapor okunabilirliginin parcasi.
        var ordered = found.OrderBy(entry => entry.Position).ToList();

        return (
            [.. ordered.Where(entry => entry.IsMetric).Select(entry => entry.Key)],
            [.. ordered.Where(entry => !entry.IsMetric).Select(entry => entry.Key)]);
    }

    private static TimeGrain ResolveGrain(IReadOnlyList<PromptToken> tokens, bool[] consumed)
    {
        (string[] Words, TimeGrain Grain)[] patterns =
        [
            (["yillik"], TimeGrain.Year),
            (["ceyreklik"], TimeGrain.Quarter),
            (["aylik"], TimeGrain.Month),
            (["haftalik"], TimeGrain.Week),
            (["gunluk"], TimeGrain.Day),
            (["yil", "bazinda"], TimeGrain.Year),
            (["ceyrek", "bazinda"], TimeGrain.Quarter),
            (["ay", "bazinda"], TimeGrain.Month),
            (["gun", "bazinda"], TimeGrain.Day)
        ];

        foreach (var (words, grain) in patterns)
        {
            var start = FindPhrase(tokens, consumed, words);

            if (start is not null)
            {
                MarkConsumed(consumed, start.Value, words.Length);
                return grain;
            }
        }

        return TimeGrain.None;
    }

    private static RequestIntent ResolveIntent(
        IReadOnlyList<PromptToken> tokens,
        bool[] consumed,
        TimeGrain grain,
        int dimensionCount)
    {
        (string[] Words, RequestIntent Intent)[] patterns =
        [
            (["karsilastir"], RequestIntent.Compare),
            (["kiyasla"], RequestIntent.Compare),
            (["trend"], RequestIntent.Trend),
            (["seyir"], RequestIntent.Trend),
            (["gidisat"], RequestIntent.Trend),
            (["listele"], RequestIntent.List),
            (["liste"], RequestIntent.List),
            (["sirala"], RequestIntent.List)
        ];

        foreach (var (words, intent) in patterns)
        {
            var start = FindPhrase(tokens, consumed, words);

            if (start is not null)
            {
                MarkConsumed(consumed, start.Value, words.Length);
                return intent;
            }
        }

        // Niyet kelimesi yok: yapidan cikarilir.
        if (grain != TimeGrain.None)
        {
            return RequestIntent.Trend;
        }

        return dimensionCount > 0 ? RequestIntent.Breakdown : RequestIntent.SingleValue;
    }

    /// <summary>
    /// Guven skoru: talebin kac kelimesinin bir anlama baglandigi. Katalog terimi, tarih
    /// ifadesi, kirilim ve niyet kelimeleri ile nezaket kelimeleri "baglandi" sayilir.
    /// </summary>
    /// <remarks>
    /// Bu bir olasilik degil, <b>kapsama oranidir</b> — deterministik bir ayristiricinin
    /// "ne kadar eminim" diye bir ic durumu yoktur. Olculebilir ve aciklanabilir olmasi,
    /// modelden gelen bir sayi tasimasindan daha degerlidir.
    /// </remarks>
    private static double ComputeConfidence(IReadOnlyList<PromptToken> tokens, bool[] consumed)
    {
        var covered = 0;

        for (var i = 0; i < tokens.Count; i++)
        {
            if (consumed[i] || FillerWords.Contains(tokens[i].Normalized))
            {
                covered++;
            }
        }

        return Math.Round((double)covered / tokens.Count, 2);
    }

    private static List<string> CollectUnresolved(IReadOnlyList<PromptToken> tokens, bool[] consumed)
    {
        var unresolved = new List<string>();

        for (var i = 0; i < tokens.Count; i++)
        {
            if (!consumed[i] && !FillerWords.Contains(tokens[i].Normalized))
            {
                unresolved.Add(tokens[i].Raw);
            }
        }

        return unresolved;
    }

    /// <summary>
    /// Kelime dizisinin, henuz tuketilmemis kelimeler uzerinde bulundugu ilk konumu dondurur.
    /// </summary>
    private static int? FindPhrase(
        IReadOnlyList<PromptToken> tokens,
        bool[] consumed,
        IReadOnlyList<string> words)
    {
        for (var start = 0; start + words.Count <= tokens.Count; start++)
        {
            var matched = true;

            for (var offset = 0; offset < words.Count; offset++)
            {
                var index = start + offset;

                if (consumed[index] || !MatchesWord(tokens[index].Normalized, words[offset]))
                {
                    matched = false;
                    break;
                }
            }

            if (matched)
            {
                return start;
            }
        }

        return null;
    }

    /// <summary>Cekim eki toleransli kelime karsilastirmasi.</summary>
    private static bool MatchesWord(string token, string word)
    {
        if (string.Equals(token, word, StringComparison.Ordinal))
        {
            return true;
        }

        if (!token.StartsWith(word, StringComparison.Ordinal))
        {
            return false;
        }

        // Uzun kokler ayirt edicidir, ek uzunlugu serbest birakilir ("kategoriye", "eyaletlere").
        // Kisa koklerde ayni serbestlik yanlis eslesme uretirdi ("ay" -> "aylik").
        return word.Length >= 4 || token.Length - word.Length <= 2;
    }

    private static void MarkConsumed(bool[] consumed, int start, int count)
    {
        for (var i = start; i < start + count && i < consumed.Length; i++)
        {
            consumed[i] = true;
        }
    }

    private static List<TermEntry> BuildTermIndex(MetricCatalogDocument catalog)
    {
        var entries = new List<TermEntry>();

        foreach (var (key, metric) in catalog.Metrics)
        {
            // Ifadesi olmayan metrik sorguda kullanilamaz; ayristirici onu hic onermez ki
            // kullanici "anlasildi" sanip bos sonuc beklemesin.
            if (metric.IsUsable)
            {
                AddEntries(entries, key, metric.Label, metric.Aliases, isMetric: true);
            }
        }

        foreach (var (key, dimension) in catalog.Dimensions)
        {
            AddEntries(entries, key, dimension.Label, dimension.Aliases, isMetric: false);
        }

        // Cok kelimeli ifadeler once denenir. Anahtara gore ikincil siralama, ayni uzunlukta
        // iki alias'in cakistigi durumda sonucun katalog dosyasindaki siraya bagli kalmasini
        // engeller: ayni girdi her zaman ayni metrige baglanmalidir.
        return
        [
            .. entries
                .OrderByDescending(entry => entry.Words.Count)
                .ThenBy(entry => entry.Key, StringComparer.Ordinal)
        ];
    }

    private static void AddEntries(
        List<TermEntry> entries,
        string key,
        string label,
        IReadOnlyList<string> aliases,
        bool isMetric)
    {
        foreach (var phrase in aliases.Concat([key, label]))
        {
            var words = TurkishTextNormalizer.Tokenize(phrase)
                .Select(token => token.Normalized)
                .Where(word => word.Length > 0)
                .ToArray();

            if (words.Length > 0)
            {
                entries.Add(new TermEntry(key, words, isMetric));
            }
        }
    }

    private sealed record TermEntry(string Key, IReadOnlyList<string> Words, bool IsMetric);
}
