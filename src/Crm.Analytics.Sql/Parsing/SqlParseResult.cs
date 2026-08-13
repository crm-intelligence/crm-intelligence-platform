using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Crm.Analytics.Sql.Parsing;

/// <summary>
/// Parse sonucu.
/// </summary>
/// <remarks>
/// ScriptDom, hata listesi dolu olsa dahi bir fragment dondurebilir. Bu yuzden basari olcutu
/// "fragment null degil" DEGIL, "hata listesi bos" olarak tanimlanmistir — kismi parse
/// edilmis bir agac uzerinde guvenlik kontrolu yapmak yanilticidir.
/// </remarks>
public sealed record SqlParseResult(TSqlFragment? Fragment, IReadOnlyList<ParseError> Errors)
{
    public bool IsSuccessful => Errors.Count == 0 && Fragment is not null;

    /// <summary>Parse basarili ise agac; degilse hata.</summary>
    public TSqlFragment RequireFragment() =>
        IsSuccessful
            ? Fragment!
            : throw new InvalidOperationException(
                "Parse basarisiz oldugu halde agac talep edildi. " +
                $"Ilk hata: {Errors.FirstOrDefault()?.Message ?? "(yok)"}");

    /// <summary>Ic teshis icin ozet hata metni. Kullaniciya gosterilmez.</summary>
    public string ErrorSummary =>
        Errors.Count == 0
            ? string.Empty
            : string.Join("; ", Errors.Take(3).Select(error => $"[{error.Number}] {error.Message}"));
}
