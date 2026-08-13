namespace Crm.Analytics.Sql.Nlu;

/// <summary>
/// Tek bir kelime: kullaniciya geri gosterilecek ham hali ve karsilastirmada kullanilacak
/// normal hali birlikte tasinir.
/// </summary>
/// <param name="Raw">Kullanicinin yazdigi hal. Cozumlenemeyen terimleri geri bildirirken kullanilir.</param>
/// <param name="Normalized">Aksansiz, kucuk harfli hal. Katalog alias'lariyla karsilastirilir.</param>
public sealed record PromptToken(string Raw, string Normalized);

/// <summary>
/// Turkce serbest metni katalog alias'lariyla karsilastirilabilir hale getirir.
/// </summary>
/// <remarks>
/// <para>
/// Katalog alias'lari aksansiz yazilmistir ("satis tutari"), kullanici ise aksanli yazar
/// ("satış tutarı"). Karsilastirmanin calismasi icin metnin tek bir kanonik forma indirilmesi
/// gerekir.
/// </para>
/// <para>
/// <b>Neden hazir <c>ToLowerInvariant</c> yetmez:</b> "İ" harfi invariant kucultmede
/// "i" + birlesik nokta (U+0307) uretir; bu iki kod birimlik dizi "i" ile esit degildir ve
/// "İSTANBUL" gibi bir girdi sessizce eslesmezdi. Bu yuzden karakter haritasi <b>kucultmeden
/// once</b> uygulanir.
/// </para>
/// </remarks>
public static class TurkishTextNormalizer
{
    /// <summary>
    /// Aksanli harflerin ASCII karsiliklari. Buyuk harfler de haritada: kucultme sonraki
    /// adimda yapilir ve "İ" bu asamada zaten "I" olmus olur.
    /// </summary>
    private static readonly Dictionary<char, char> AsciiFolding = new()
    {
        ['ı'] = 'i', ['İ'] = 'I', ['ş'] = 's', ['Ş'] = 'S', ['ğ'] = 'g', ['Ğ'] = 'G',
        ['ü'] = 'u', ['Ü'] = 'U', ['ö'] = 'o', ['Ö'] = 'O', ['ç'] = 'c', ['Ç'] = 'C',
        ['â'] = 'a', ['Â'] = 'A', ['î'] = 'i', ['Î'] = 'I', ['û'] = 'u', ['Û'] = 'U',
        ['é'] = 'e', ['É'] = 'E', ['ã'] = 'a', ['Ã'] = 'A', ['á'] = 'a', ['Á'] = 'A'
    };

    /// <summary>
    /// Kelime icinde korunan isaretler. Tarih ifadeleri ("2018-01-15", "2018/01/15") bu
    /// isaretler atilirsa uc ayri sayiya bolunur ve tarih olarak taninamazdi. Alt cizgi
    /// katalog anahtarlari icin korunur: "item_sales" atilirsa "itemsales" olur ve anahtarla
    /// eslesmezdi.
    /// </summary>
    private const string PreservedInsideWord = "-/._";

    /// <summary>Metni aksansiz, kucuk harfli, tek bosluklu hale getirir.</summary>
    public static string Normalize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return string.Join(' ', Tokenize(text).Select(token => token.Normalized));
    }

    /// <summary>
    /// Metni kelimelere ayirir. Ayirici bosluktur; kelimenin bas ve sonundaki noktalama
    /// kirpilir, icindeki tarih isaretleri korunur.
    /// </summary>
    public static IReadOnlyList<PromptToken> Tokenize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var tokens = new List<PromptToken>();

        foreach (var raw in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = Trim(raw);

            if (trimmed.Length == 0)
            {
                continue;
            }

            tokens.Add(new PromptToken(trimmed, Fold(trimmed)));
        }

        return tokens;
    }

    /// <summary>Bas ve sondaki, kelimeye ait olmayan isaretleri kirpar.</summary>
    private static string Trim(string word)
    {
        var start = 0;
        var end = word.Length - 1;

        while (start <= end && !IsWordCharacter(word[start]))
        {
            start++;
        }

        while (end >= start && !IsWordCharacter(word[end]))
        {
            end--;
        }

        return start > end ? string.Empty : word[start..(end + 1)];
    }

    private static bool IsWordCharacter(char value) => char.IsLetterOrDigit(value);

    private static string Fold(string word)
    {
        var buffer = new char[word.Length];
        var length = 0;

        foreach (var character in word)
        {
            var folded = AsciiFolding.TryGetValue(character, out var replacement)
                ? replacement
                : character;

            if (char.IsLetterOrDigit(folded) || PreservedInsideWord.Contains(folded))
            {
                buffer[length++] = char.ToLowerInvariant(folded);
            }
        }

        return new string(buffer, 0, length);
    }
}
