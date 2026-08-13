using System.Globalization;
using Crm.Analytics.Sql.Contracts;

namespace Crm.Analytics.Sql.Nlu;

/// <summary>
/// Metinde bulunan tarih ifadesi ve onun kapladigi kelime araligi.
/// </summary>
/// <param name="Range">Cozumlenmis tarih araligi.</param>
/// <param name="TokenStart">Ifadenin basladigi kelime indeksi.</param>
/// <param name="TokenCount">Ifadenin kapladigi kelime sayisi.</param>
public sealed record DateRangeMatch(DateRangeSpec Range, int TokenStart, int TokenCount);

/// <summary>
/// Turkce tarih ifadelerini mutlak araliga cevirir.
/// </summary>
/// <remarks>
/// <para>
/// Cozumleme <b>deterministiktir</b>: bugunun tarihi cagirandan gelir, sistem saatinden
/// okunmaz. Ayni girdi ayni referans gunle her zaman ayni araligi verir; aksi halde ne test
/// edilebilir ne de audit'te yeniden uretilebilir olurdu.
/// </para>
/// <para>
/// <b>Gorece ifadelerde de mutlak tarihler yazilir.</b> "gecen ceyrek" ifadesi SQL'e
/// gomulmez; parametreye baglanacak kesin tarihler burada hesaplanir, ifadenin kendisi
/// yalnizca gerekce olarak <see cref="DateRangeSpec.RelativeExpression"/> alaninda saklanir.
/// </para>
/// <para>
/// <b>Desteklenmeyen:</b> "1 Ocak - 31 Mart" gibi gun-ay ciftleri ve "tum zamanlar" gibi
/// sinirsiz araliklar. Ikincisi bilincli olarak yoktur: sinirsiz aralik
/// <c>DateRangeBudget</c> kontrolunu ihlal eder, kullaniciya once daralt demek dogrudur.
/// </para>
/// </remarks>
public static class RelativeDateResolver
{
    private static readonly IReadOnlyDictionary<string, int> TurkishNumberWords =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["bir"] = 1, ["iki"] = 2, ["uc"] = 3, ["dort"] = 4,
            ["bes"] = 5, ["alti"] = 6, ["yedi"] = 7, ["sekiz"] = 8,
            ["dokuz"] = 9, ["on"] = 10, ["yirmi"] = 20, ["otuz"] = 30,
            ["kirk"] = 40, ["elli"] = 50, ["altmis"] = 60,
            ["yetmis"] = 70, ["seksen"] = 80, ["doksan"] = 90
        };

    private static readonly HashSet<string> TurkishDateSuffixes = new(StringComparer.Ordinal)
    {
        "", "a", "e", "i", "u", "in", "un", "nin", "nun",
        "da", "de", "ta", "te", "dan", "den", "tan", "ten",
        "ki", "ku", "lik", "lik", "inda", "inde", "unda", "unde",
        "sinda", "sinde", "sunda", "sunde", "indaki", "indeki",
        "undaki", "undeki", "lar", "ler", "larda", "lerde"
    };

    private static readonly string[] MonthNames =
    [
        "ocak", "subat", "mart", "nisan", "mayis", "haziran",
        "temmuz", "agustos", "eylul", "ekim", "kasim", "aralik"
    ];

    private const int MinimumYear = 1990;
    private const int MaximumYear = 2100;

    public static DateRangeMatch? Resolve(IReadOnlyList<PromptToken> tokens, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(tokens);

        // Sira en ozgulden genele: "ocak 2018" ifadesi yalniz-yil kalibina da uyar,
        // once ay+yil denenmezse ifadenin ay bilgisi sessizce kaybolurdu.
        return MatchAbsolutePair(tokens)
            ?? MatchMonthAndYear(tokens)
            ?? MatchRelativePhrase(tokens, today)
            ?? MatchYearOnly(tokens);
    }

    /// <summary>
    /// Resolves a schema-constrained semantic date token emitted by a planner. The model
    /// supplies meaning only; all calendar arithmetic remains deterministic in the backend.
    /// </summary>
    public static DateRangeSpec? ResolveSemanticExpression(
        string expression,
        int? count,
        DateOnly today)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var quarterStart = new DateOnly(
            today.Year, (((today.Month - 1) / 3) * 3) + 1, 1);
        var weekStart = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));

        return expression switch
        {
            "today" => Range(today, today, "today"),
            "yesterday" => Range(today.AddDays(-1), today.AddDays(-1), "yesterday"),
            "current_week" => Range(weekStart, today, "current_week"),
            "previous_week" => Range(
                weekStart.AddDays(-7), weekStart.AddDays(-1), "previous_week"),
            "current_month" => Range(monthStart, today, "current_month"),
            "previous_month" => Range(
                monthStart.AddMonths(-1), monthStart.AddDays(-1), "previous_month"),
            "current_quarter" => Range(quarterStart, today, "current_quarter"),
            "previous_quarter" => Range(
                quarterStart.AddMonths(-3), quarterStart.AddDays(-1),
                "previous_quarter"),
            "current_year" => Range(
                new DateOnly(today.Year, 1, 1), today, "current_year"),
            "previous_year" => Range(
                new DateOnly(today.Year - 1, 1, 1),
                new DateOnly(today.Year - 1, 12, 31), "previous_year"),
            "last_n_days" when count is > 0 and <= 3660 => Range(
                today.AddDays(-(count.Value - 1)), today, $"last_{count}_days"),
            "last_n_weeks" when count is > 0 and <= 520 => Range(
                today.AddDays(-((count.Value * 7) - 1)), today,
                $"last_{count}_weeks"),
            "last_n_months" when count is > 0 and <= 120 => Range(
                today.AddMonths(-count.Value).AddDays(1), today,
                $"last_{count}_months"),
            "last_n_years" when count is > 0 and <= 10 => Range(
                today.AddYears(-count.Value).AddDays(1), today,
                $"last_{count}_years"),
            _ => null
        };
    }

    /// <summary>
    /// "2018-01-01 ile 2018-03-31" — mutlak tarihler. Metindeki ilk iki tarih araligi olusturur;
    /// aradaki kelimeler ("ile", "tarihinden", "arasi") onemsizdir. Iki tarih yazan kullanici
    /// aralik kastediyor kabul edilir.
    /// </summary>
    private static DateRangeMatch? MatchAbsolutePair(IReadOnlyList<PromptToken> tokens)
    {
        var found = new List<(int Index, DateOnly Value)>();

        for (var i = 0; i < tokens.Count && found.Count < 2; i++)
        {
            if (TryParseIsoDate(tokens[i].Normalized, out var value))
            {
                found.Add((i, value));
            }
        }

        if (found.Count == 0)
        {
            return null;
        }

        if (found.Count == 1)
        {
            // Tek tarih: o gunun kendisi.
            return new DateRangeMatch(
                new DateRangeSpec
                {
                    Kind = DateRangeKind.Absolute,
                    From = found[0].Value,
                    To = found[0].Value
                },
                found[0].Index,
                1);
        }

        // Ters sirada yazilmis olabilir; oldugu gibi tasimak bos sonuc kumesi uretirdi.
        var start = found[0].Value <= found[1].Value ? found[0].Value : found[1].Value;
        var end = found[0].Value <= found[1].Value ? found[1].Value : found[0].Value;

        return new DateRangeMatch(
            new DateRangeSpec { Kind = DateRangeKind.Absolute, From = start, To = end },
            found[0].Index,
            found[1].Index - found[0].Index + 1);
    }

    /// <summary>"ocak 2018" veya "2018 ocak" — tek ayin tamami.</summary>
    private static DateRangeMatch? MatchMonthAndYear(IReadOnlyList<PromptToken> tokens)
    {
        for (var i = 0; i + 1 < tokens.Count; i++)
        {
            var first = tokens[i].Normalized;
            var second = tokens[i + 1].Normalized;

            var month = IndexOfMonth(first);
            var year = TryParseYear(second);

            if (month is null || year is null)
            {
                month = IndexOfMonth(second);
                year = TryParseYear(first);
            }

            if (month is null || year is null)
            {
                continue;
            }

            var from = new DateOnly(year.Value, month.Value, 1);

            return new DateRangeMatch(
                new DateRangeSpec
                {
                    Kind = DateRangeKind.Absolute,
                    From = from,
                    To = from.AddMonths(1).AddDays(-1)
                },
                i,
                2);
        }

        return null;
    }

    /// <summary>"bu yil", "gecen ceyrek", "son 30 gun" gibi gorece ifadeler.</summary>
    private static DateRangeMatch? MatchRelativePhrase(IReadOnlyList<PromptToken> tokens, DateOnly today)
    {
        for (var i = 0; i < tokens.Count; i++)
        {
            var word = tokens[i].Normalized;

            if (StartsWithStem(word, "bugun"))
            {
                return Relative(today, today, "today", i, 1);
            }

            if (StartsWithStem(word, "dun"))
            {
                var yesterday = today.AddDays(-1);
                return Relative(yesterday, yesterday, "yesterday", i, 1);
            }

            if (i + 1 >= tokens.Count)
            {
                continue;
            }

            var next = tokens[i + 1].Normalized;

            // "son 30 gun" / "son otuz gun" / "son 2 yil"
            if (IsRecentMarker(word) && TryReadCount(tokens, i + 1,
                    out var count, out var countTokenCount)
                && i + 1 + countTokenCount < tokens.Count)
            {
                var unit = tokens[i + 1 + countTokenCount].Normalized;
                var span = ResolveCountedSpan(today, count, unit);

                if (span is not null)
                {
                    return new DateRangeMatch(span, i, countTokenCount + 2);
                }
            }

            var phrase = ResolveTwoWordPhrase(word, next, today);

            if (phrase is not null)
            {
                return new DateRangeMatch(phrase, i, 2);
            }
        }

        return null;
    }

    /// <summary>"2018" — yilin tamami.</summary>
    private static DateRangeMatch? MatchYearOnly(IReadOnlyList<PromptToken> tokens)
    {
        for (var i = 0; i < tokens.Count; i++)
        {
            var year = TryParseYear(tokens[i].Normalized);

            if (year is null)
            {
                continue;
            }

            var start = i;
            var count = 1;
            if (i + 1 < tokens.Count && StartsWithStem(tokens[i + 1].Normalized, "yil"))
            {
                count = 2;
            }
            else if (i > 0 && StartsWithStem(tokens[i - 1].Normalized, "yil"))
            {
                start = i - 1;
                count = 2;
            }

            return new DateRangeMatch(
                new DateRangeSpec
                {
                    Kind = DateRangeKind.Absolute,
                    From = new DateOnly(year.Value, 1, 1),
                    To = new DateOnly(year.Value, 12, 31)
                },
                start,
                count);
        }

        return null;
    }

    private static DateRangeSpec? ResolveTwoWordPhrase(string first, string second, DateOnly today)
    {
        var isCurrent = first is "bu";
        var isPrevious = first is "gecen" or "onceki" or "gectigimiz" or "son";

        if (!isCurrent && !isPrevious)
        {
            return null;
        }

        // Ekli haller de kabul edilir: "yila", "ayin", "ceyrekte".
        if (StartsWithStem(second, "yil") || StartsWithStem(second, "sene"))
        {
            return isCurrent
                ? Range(new DateOnly(today.Year, 1, 1), today, "this_year")
                : Range(new DateOnly(today.Year - 1, 1, 1), new DateOnly(today.Year - 1, 12, 31), "last_year");
        }

        if (StartsWithStem(second, "ay"))
        {
            var monthStart = new DateOnly(today.Year, today.Month, 1);

            return isCurrent
                ? Range(monthStart, today, "this_month")
                : Range(monthStart.AddMonths(-1), monthStart.AddDays(-1), "last_month");
        }

        if (StartsWithStem(second, "ceyrek"))
        {
            var quarterStart = new DateOnly(today.Year, (((today.Month - 1) / 3) * 3) + 1, 1);

            return isCurrent
                ? Range(quarterStart, today, "this_quarter")
                : Range(quarterStart.AddMonths(-3), quarterStart.AddDays(-1), "last_quarter");
        }

        if (StartsWithStem(second, "hafta"))
        {
            // Hafta basi gun adina degil, referans gune gore 7 gunluk pencereye baglanir:
            // hafta basinin gune bagli tanimi sunucu ayarina (DATEFIRST) gore degisirdi.
            return isCurrent
                ? Range(today.AddDays(-6), today, "last_7_days")
                : Range(today.AddDays(-13), today.AddDays(-7), "previous_7_days");
        }

        return null;
    }

    private static DateRangeSpec? ResolveCountedSpan(DateOnly today, int count, string unit)
    {
        if (StartsWithStem(unit, "gun"))
        {
            return Range(today.AddDays(-(count - 1)), today, $"last_{count}_days");
        }

        if (StartsWithStem(unit, "ay"))
        {
            return Range(today.AddMonths(-count).AddDays(1), today, $"last_{count}_months");
        }

        if (StartsWithStem(unit, "yil") || StartsWithStem(unit, "sene"))
        {
            return Range(today.AddYears(-count).AddDays(1), today, $"last_{count}_years");
        }

        if (StartsWithStem(unit, "hafta"))
        {
            return Range(today.AddDays(-((count * 7) - 1)), today, $"last_{count * 7}_days");
        }

        return null;
    }

    private static bool IsRecentMarker(string word) => word is "son" or "gecen" or "onceki";

    /// <summary>
    /// Turkce cekim eki toleransi: kok, kelimenin onekiyse ve fazlalik en fazla uc karakterse
    /// eslesir. Bu bir morfolojik cozumleme degildir; "ay" gibi kisa koklerde yanlis eslesme
    /// riskini sinirlamak icin fazlalik uzunlugu bilincli olarak dar tutulmustur.
    /// </summary>
    private static bool StartsWithStem(string word, string stem)
    {
        if (!word.StartsWith(stem, StringComparison.Ordinal))
        {
            return false;
        }

        return TurkishDateSuffixes.Contains(word[stem.Length..]);
    }

    private static bool TryReadCount(
        IReadOnlyList<PromptToken> tokens,
        int start,
        out int count,
        out int tokenCount)
    {
        count = 0;
        tokenCount = 0;
        if (start >= tokens.Count)
        {
            return false;
        }

        var first = tokens[start].Normalized;
        if (int.TryParse(first, NumberStyles.None, CultureInfo.InvariantCulture,
                out count) && count > 0)
        {
            tokenCount = 1;
            return true;
        }

        if (!TurkishNumberWords.TryGetValue(first, out count) || count <= 0)
        {
            return false;
        }

        tokenCount = 1;
        if (count >= 10 && start + 1 < tokens.Count
            && TurkishNumberWords.TryGetValue(tokens[start + 1].Normalized,
                out var ones)
            && ones is > 0 and < 10)
        {
            count += ones;
            tokenCount = 2;
        }
        return true;
    }

    private static int? IndexOfMonth(string word)
    {
        for (var i = 0; i < MonthNames.Length; i++)
        {
            if (StartsWithStem(word, MonthNames[i]))
            {
                return i + 1;
            }
        }

        var fuzzyMatches = new List<int>();
        for (var i = 0; i < MonthNames.Length; i++)
        {
            if (FuzzyMonthMatches(word, MonthNames[i]))
            {
                fuzzyMatches.Add(i + 1);
            }
        }

        return fuzzyMatches.Count == 1 ? fuzzyMatches[0] : null;
    }

    private static bool FuzzyMonthMatches(string word, string month)
    {
        foreach (var suffix in TurkishDateSuffixes.OrderByDescending(value => value.Length))
        {
            if (!word.EndsWith(suffix, StringComparison.Ordinal))
            {
                continue;
            }

            var candidate = suffix.Length == 0
                ? word
                : word[..^suffix.Length];
            if (DamerauLevenshteinDistanceAtMostOne(candidate, month))
            {
                return true;
            }
        }

        return false;
    }

    private static bool DamerauLevenshteinDistanceAtMostOne(
        string left,
        string right)
    {
        if (left == right)
        {
            return true;
        }
        if (Math.Abs(left.Length - right.Length) > 1)
        {
            return false;
        }

        if (left.Length == right.Length)
        {
            var differences = new List<int>(2);
            for (var i = 0; i < left.Length; i++)
            {
                if (left[i] != right[i])
                {
                    differences.Add(i);
                    if (differences.Count > 2)
                    {
                        return false;
                    }
                }
            }

            return differences.Count == 1
                || differences.Count == 2
                    && differences[1] == differences[0] + 1
                    && left[differences[0]] == right[differences[1]]
                    && left[differences[1]] == right[differences[0]];
        }

        var shorter = left.Length < right.Length ? left : right;
        var longer = left.Length < right.Length ? right : left;
        var shortIndex = 0;
        var longIndex = 0;
        var skipped = false;
        while (shortIndex < shorter.Length && longIndex < longer.Length)
        {
            if (shorter[shortIndex] == longer[longIndex])
            {
                shortIndex++;
                longIndex++;
                continue;
            }
            if (skipped)
            {
                return false;
            }
            skipped = true;
            longIndex++;
        }
        return true;
    }

    private static int? TryParseYear(string word) =>
        int.TryParse(word, NumberStyles.None, CultureInfo.InvariantCulture, out var year)
        && year is >= MinimumYear and <= MaximumYear
            ? year
            : null;

    private static bool TryParseIsoDate(string word, out DateOnly value) =>
        DateOnly.TryParseExact(word, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out value)
        || DateOnly.TryParseExact(word, "yyyy/MM/dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out value)
        || DateOnly.TryParseExact(word, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out value);

    private static DateRangeSpec Range(DateOnly from, DateOnly to, string expression) =>
        new()
        {
            Kind = DateRangeKind.Relative,
            RelativeExpression = expression,
            From = from,
            To = to
        };

    private static DateRangeMatch Relative(
        DateOnly from, DateOnly to, string expression, int start, int count) =>
        new(Range(from, to, expression), start, count);
}
