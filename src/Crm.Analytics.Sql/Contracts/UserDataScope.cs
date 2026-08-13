namespace Crm.Analytics.Sql.Contracts;

/// <summary>
/// Kullanicinin veri kapsami (yetki). Guardrail bu bilgiyi SQL'e <b>zorla enjekte eder</b>;
/// sorguda kapsam filtresi olup olmadigini kontrol etmez.
/// </summary>
/// <remarks>
/// <para>
/// <b>Fail-closed tasarim:</b> bos kapsam ile sinirsiz kapsam ayri seylerdir ve birbirine
/// donusmez. Bos <see cref="Dimensions"/> + <see cref="IsUnrestricted"/> false, kullanicinin
/// yetkisinin belirlenemedigi anlamina gelir ve <see cref="ReasonCode.GR007"/> ile reddedilir.
/// "Tum bolgeleri gorebilen" kullanici icin <see cref="Unrestricted"/> acikca kullanilmalidir.
/// Bu ayrimin kaybolmasi, yetkisi cozumlenemeyen bir kullanicinin her seyi gormesi demektir.
/// </para>
/// <para>
/// Cok boyutlu kapsam (sirket / bolge / magaza / departman) desteklenecek sekilde
/// modellenmistir; sprintin bu asamasinda yalnizca "region" boyutu doldurulur.
/// </para>
/// </remarks>
public sealed record UserDataScope
{
    /// <summary>Region boyutunun kanonik adi. Allow-list'teki scopeColumn ile eslesir.</summary>
    public const string RegionDimension = "region";

    private UserDataScope(
        IReadOnlyDictionary<string, IReadOnlyList<string>> dimensions,
        bool isUnrestricted)
    {
        Dimensions = dimensions;
        IsUnrestricted = isUnrestricted;
    }

    /// <summary>
    /// Boyut adi -> izinli deger listesi. Bir boyut burada yoksa o boyut icin kisitlama
    /// uygulanmaz; bu yuzden bos sozluk "her sey serbest" gibi davranamaz —
    /// <see cref="IsUnrestricted"/> bayragi olmadan bos kapsam ret sebebidir.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Dimensions { get; }

    /// <summary>
    /// Kullanici tum veriyi gorme yetkisine sahip mi. Yalnizca acik karar sonucu true olur;
    /// kapsam cozumlenememesi bu bayragi true yapmaz.
    /// </summary>
    public bool IsUnrestricted { get; }

    /// <summary>
    /// Kapsam kullanilabilir mi. false ise guardrail <see cref="ReasonCode.GR007"/> dondurur.
    /// </summary>
    public bool IsResolvable => IsUnrestricted || Dimensions.Any(entry => entry.Value.Count > 0);

    /// <summary>Tum veriye erisim. Cagiran taraf bu karari acikca vermis olur.</summary>
    public static UserDataScope Unrestricted { get; } =
        new(new Dictionary<string, IReadOnlyList<string>>(), isUnrestricted: true);

    /// <summary>
    /// Kapsami cozumlenemeyen kullanici. Guardrail bunu reddeder — varsayilan deger olarak
    /// bilincli sekilde "en kisitlayici" durum secilmistir.
    /// </summary>
    public static UserDataScope Unresolved { get; } =
        new(new Dictionary<string, IReadOnlyList<string>>(), isUnrestricted: false);

    /// <summary>Yalnizca bolge kapsami olan kullanici.</summary>
    public static UserDataScope ForRegions(params string[] regions) =>
        ForDimension(RegionDimension, regions);

    /// <summary>Tek bir boyut uzerinden kapsam.</summary>
    public static UserDataScope ForDimension(string dimension, params string[] values)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dimension);
        ArgumentNullException.ThrowIfNull(values);

        // Bos deger listesiyle cagirmak "kisitlama yok" demek DEGILDIR; cozumlenemeyen
        // kapsam olarak ele alinir ve ret uretir.
        if (values.Length == 0)
        {
            return Unresolved;
        }

        var dimensions = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [dimension] = values.ToArray()
        };

        return new UserDataScope(dimensions, isUnrestricted: false);
    }

    /// <summary>Verilen boyut icin izinli degerler. Kisitlama yoksa bos liste doner.</summary>
    public IReadOnlyList<string> ValuesFor(string dimension) =>
        Dimensions.TryGetValue(dimension, out var values) ? values : [];
}
