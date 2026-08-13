namespace Crm.Analytics.Sql.Contracts;

/// <summary>
/// Talebin tarih araligi. Guardrail bu araligi hem parametre olarak baglar hem de
/// <c>DateRangeBudget</c> kontrolunde ust sinira karsi denetler: donen satiri sinirlamak
/// (TOP) taranan satiri sinirlamaz, bu yuzden aralik ayrica butcelenir.
/// </summary>
public sealed record DateRangeSpec
{
    public required DateRangeKind Kind { get; init; }

    /// <summary>
    /// <see cref="DateRangeKind.Relative"/> icin gorece ifade ("last_quarter", "this_year").
    /// Cozumlenmis mutlak tarihler <see cref="From"/> ve <see cref="To"/> alanlarina yazilir;
    /// bu alan cozumlemenin gerekcesini audit'te gorunur kilmak icin korunur.
    /// </summary>
    public string? RelativeExpression { get; init; }

    /// <summary>Aralik baslangici (dahil).</summary>
    public DateOnly? From { get; init; }

    /// <summary>Aralik bitisi (dahil).</summary>
    public DateOnly? To { get; init; }

    /// <summary>
    /// Tarih araligi <b>uygulanamayan</b> talep: kaynakta zaman boyutu yoktur
    /// (ornek: <c>vw_customer_rfm</c>). Bos bir aralik ile "belirtilmemis" araligi ayirt
    /// etmek onemlidir — ikincisi zaman boyutu tasiyan bir kaynakta hata sebebidir.
    /// </summary>
    public static readonly DateRangeSpec NotApplicable = new() { Kind = DateRangeKind.NotApplicable };
}
