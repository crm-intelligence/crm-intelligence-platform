namespace Crm.Analytics.Sql.Contracts;

/// <summary>
/// Filtre degerinin tipi. Guardrail bu bilgiyi <c>SqlParameterSpec</c> uretirken kullanir;
/// tip bilgisi kaybolursa implicit conversion olusur, index seek kaybedilir ve en kotusu
/// collation'a bagli olarak farkli satir kumesi doner.
/// </summary>
public enum FilterValueKind
{
    /// <summary>
    /// Metin. Guardrail bunu her zaman <c>NVARCHAR</c> olarak baglar. Varsayilan olarak
    /// unicode secilmesi bilinclidir: bolge ve segment adlari Turkce karakter icerir ve
    /// <c>VARCHAR</c> baglamak Turkce I/i cifti uzerinden filtre atlatmaya zemin hazirlar.
    /// </summary>
    Text,

    Integer,

    Decimal,

    Boolean,

    Date
}

/// <summary>
/// Tek bir filtre degeri. Ham metin olarak tasinir, tip <see cref="Kind"/> ile ayri belirtilir.
/// Deger hicbir asamada SQL metnine gomulmez; yalnizca parametre olarak baglanir.
/// </summary>
/// <param name="Kind">Degerin tipi.</param>
/// <param name="Raw">Degerin ham metin gosterimi. Tirnak veya escape icermez.</param>
public sealed record FilterLiteral(FilterValueKind Kind, string Raw);

/// <summary>
/// Canonical Request icindeki tek bir filtre kosulu.
/// </summary>
/// <remarks>
/// Deger sayisi ile operator uyumu (<see cref="FilterOperator.Eq"/> tek deger,
/// <see cref="FilterOperator.Between"/> iki deger, <see cref="FilterOperator.In"/> en az bir deger)
/// burada dogrulanmaz. Bu bir tasima sozlesmesidir; dogrulama Query Builder ve guardrail
/// kontrollerinin isidir. Sozlesmeye is mantigi konulmamasi bilinclidir.
/// </remarks>
public sealed record RequestFilter
{
    /// <summary>
    /// Filtrelenecek alan. Yalnizca Metric Catalog'daki dimension anahtarlarindan biri olabilir;
    /// serbest metin kolon adi kabul edilmez.
    /// </summary>
    public required string Field { get; init; }

    public required FilterOperator Op { get; init; }

    /// <summary>Operatorun bekledigi sayida deger.</summary>
    public required IReadOnlyList<FilterLiteral> Values { get; init; }
}
