namespace Crm.Analytics.Sql.Contracts;

/// <summary>
/// Uretilen SQL'e baglanacak tek bir parametre. Bilincli olarak <c>SqlParameter</c> degildir:
/// guardrail hicbir veritabani bagimliligi tasimaz, boylece tum kontroller ve mutasyonlar
/// canli baglanti olmadan test edilebilir.
/// </summary>
/// <param name="Name">Parametre adi, '@' isareti dahil (ScriptDom'un VariableReference.Name bicimi).</param>
/// <param name="Kind">Deger tipi. Yanlis tip baglamak implicit conversion ve index seek kaybi demektir.</param>
/// <param name="Raw">Degerin ham metin gosterimi. Tirnak veya escape icermez.</param>
public sealed record SqlParameterSpec(string Name, FilterValueKind Kind, string Raw)
{
    /// <summary>
    /// Metin parametreleri her zaman unicode (NVARCHAR) baglanir. Bolge ve segment adlari
    /// Turkce karakter icerir; VARCHAR baglamak collation'a bagli olarak farkli satir kumesi
    /// dondurebilir ve Turkce I/i cifti uzerinden filtre atlatmaya zemin hazirlar.
    /// </summary>
    public bool IsUnicode => Kind == FilterValueKind.Text;

    /// <summary>
    /// Deger 8000 bayti asiyor mu. Asiyorsa execution katmani MAX uzunluk kullanmali,
    /// aksi halde deger sessizce kirpilir.
    /// </summary>
    public bool IsLargeObject => Kind == FilterValueKind.Text && Raw.Length > 4000;
}
