namespace Crm.Analytics.Sql.Contracts;

/// <summary>
/// Talebin ne tur bir sonuc bekledigi. Result Shape Classifier gorsel tipi onerisini
/// buna ve sonuc setinin sekline gore uretir.
/// </summary>
public enum RequestIntent
{
    /// <summary>Iki veya daha fazla degeri karsilastirma (ornek: "Marmara ve Ege'yi karsilastir").</summary>
    Compare,

    /// <summary>Zaman ekseninde seyir (ornek: "bu yil aylik satis trendi").</summary>
    Trend,

    /// <summary>Bir boyuta gore kirilim (ornek: "net satisi bolgeye gore goster").</summary>
    Breakdown,

    /// <summary>Tek skaler deger (ornek: "bugunku toplam satis").</summary>
    SingleValue,

    /// <summary>Satir listesi (ornek: "en cok satan 10 urun").</summary>
    List
}

/// <summary>
/// Zaman kirilimi. <see cref="None"/>, zaman ekseni olmayan talepleri ifade eder;
/// bu durumda tarih araligi filtre olarak uygulanir ama GROUP BY'a girmez.
/// </summary>
public enum TimeGrain
{
    None,
    Day,
    Week,
    Month,
    Quarter,
    Year
}

/// <summary>
/// Filtre karsilastirma operatoru. Serbest metin operator kabul edilmez; guardrail
/// yalnizca bu kumeyi AST'ye cevirebilir.
/// </summary>
public enum FilterOperator
{
    Eq,
    NotEq,
    In,
    NotIn,
    Gt,
    Gte,
    Lt,
    Lte,
    Between
}

public enum SortDirection
{
    Asc,
    Desc
}

/// <summary>
/// Tarih araliginin nasil ifade edildigi.
/// </summary>
public enum DateRangeKind
{
    /// <summary>Isle iliskili gorece ifade ("last_quarter"). Cozumleme takvim kurallarina baglidir.</summary>
    Relative,

    /// <summary>Kesin baslangic ve bitis tarihi.</summary>
    Absolute,

    /// <summary>
    /// Tarih araligi <b>uygulanamaz</b>: kaynakta zaman boyutu yok (ornek: <c>vw_customer_rfm</c>).
    /// </summary>
    /// <remarks>
    /// Ayri bir tur olmasi bilincli. Bu durumu <see cref="Absolute"/> + bos tarihler olarak
    /// ifade etmek, sozlesmenin "absolute ise from/to zorunlu" kuralini ihlal ederdi ve
    /// "belirtilmemis aralik" ile "uygulanamaz aralik" ayirt edilemezdi — ilki zaman boyutu
    /// tasiyan bir kaynakta hata sebebidir, ikincisi normal durumdur.
    /// </remarks>
    NotApplicable
}
