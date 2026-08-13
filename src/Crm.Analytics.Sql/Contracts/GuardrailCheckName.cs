namespace Crm.Analytics.Sql.Contracts;

/// <summary>
/// Guardrail kontrolleri, <b>calisma sirasiyla</b>. Enum degerleri sirayi tanimlar; pipeline
/// bu siraya gore kosar ve ilk basarisiz kontrolde durur.
/// </summary>
/// <remarks>
/// Proje dokumaninda 12 kontrol tanimlidir. Buradaki set 16 kontrole cikarilmistir:
/// <see cref="InputLimits"/>, <see cref="NodeTypeWhitelist"/>, <see cref="MinCellSize"/>,
/// <see cref="DateRangeBudget"/> ve <see cref="RegenerateAndRevalidate"/> eklenmistir.
/// Gerekce: dokumandaki set, CTE/alt sorgu uzerinden kapsam atlatma, PII'nin WHERE uzerinden
/// sizdirilmasi, kimlik seviyesinde detay ve mutasyon sonrasi dogrulama bosluklarini
/// kapatmiyordu.
/// </remarks>
public enum GuardrailCheckName
{
    /// <summary>Girdi boyutu makul mu (kaynak tuketimine karsi ilk bariyer).</summary>
    InputLimits = 0,

    /// <summary>
    /// Sorgu gecerli T-SQL olarak parse edilebiliyor. Regex YETMEZ.
    /// </summary>
    /// <remarks>
    /// Proje dokumaninda bu kontrol 2. sirada, <see cref="SingleStatement"/> 1. sirada yer alir.
    /// Sira <b>bilincli olarak degistirilmistir</b>: parse edilmemis bir metinde ';' saymak
    /// ancak metin/regex ile yapilabilir ve bu, "kontroller regex ile yapilmaz" kirmizi
    /// cizgisini ihlal ederdi. Ayrica string icindeki ';' (ornek: <c>WHERE x = 'a;b'</c>)
    /// yanlis pozitif, yorum icindeki ';' ise yanlis negatif uretirdi. Once parse edilir,
    /// ifade sayimi AST uzerinden yapilir.
    /// </remarks>
    ParseToAst = 1,

    /// <summary>Tek bir ifade var; ';' ile ayrilmis zincir yok. AST uzerinden sayilir.</summary>
    SingleStatement = 2,

    /// <summary>AST koku SELECT; alt agacta DML/DDL/EXEC yok.</summary>
    SelectOnly = 3,

    /// <summary>SELECT * kullanilmiyor (nitelenmis 'v.*' dahil).</summary>
    NoStarSelect = 4,

    /// <summary>Beyaz listede olmayan SQL yapisi yok (OPENROWSET/OPENJSON/APPLY/inline TVF).</summary>
    NodeTypeWhitelist = 5,

    /// <summary>Tum FROM/JOIN hedefleri allow-list'te.</summary>
    AllowListObjects = 6,

    /// <summary>
    /// Yasakli PII kolonlari agacin HICBIR yerinde kullanilmamis (yalnizca SELECT degil).
    /// </summary>
    /// <remarks>
    /// Bu kontrol <see cref="AllowListColumns"/>'tan ONCE calisir. Sira bilincli:
    /// PII kolonlari allow-list'te bulunmadigi icin, allow-list kontrolu once kosarsa bu
    /// kontrol hicbir zaman tetiklenmez (olu kod olur) ve bir PII erisim denemesi audit'e
    /// "bilinmeyen kolon" (GR004) olarak, yani yazim hatasiyla ayni sekilde yazilirdi.
    /// PII denemesi bir guvenlik olayidir ve kendi gerekce koduyla (GR005) kaydedilmelidir.
    /// </remarks>
    NoDeniedPiiColumns = 7,

    /// <summary>Referans edilen tum kolonlar allow-list'te.</summary>
    AllowListColumns = 8,

    /// <summary>JOIN yollari tanimli ve sayisi maxJoins'i asmiyor.</summary>
    JoinPathAllowed = 9,

    /// <summary>Kimlik seviyesinde detay veya tek kayit izole eden kirilim yok.</summary>
    MinCellSize = 10,

    /// <summary>Tarih araligi ust siniri asilmamis.</summary>
    DateRangeBudget = 11,

    /// <summary>Veri kapsami filtresi agactaki HER sorgu blokuna enjekte edildi.</summary>
    ScopeFilterInjection = 12,

    /// <summary>Tum literal degerler parametreye cevrildi.</summary>
    LiteralParameterization = 13,

    /// <summary>Satir siniri uygulandi.</summary>
    RowLimit = 14,

    /// <summary>
    /// Mutasyon sonrasi SQL yeniden uretildi, yeniden parse edildi ve okuma kontrolleri
    /// bastan kosuldu. Pazarlik disi: mutasyon yapan kontroller kendi acigini yaratabilir.
    /// </summary>
    RegenerateAndRevalidate = 15
}
