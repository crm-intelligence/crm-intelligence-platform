using Crm.Analytics.Sql.Contracts;

namespace Crm.Analytics.Sql.QueryBuilder;

/// <summary>
/// Query Builder'in ciktisi. Uretilen SQL <b>henuz onaylanmis degildir</b> — guardrail
/// hattindan gecmesi zorunludur.
/// </summary>
public sealed class QueryBuildResult
{
    private QueryBuildResult(
        bool isSuccessful,
        string? sql,
        IReadOnlyList<SqlParameterSpec> parameters,
        string? sourceObject,
        ReasonCode reasonCode,
        string? detail)
    {
        IsSuccessful = isSuccessful;
        Sql = sql;
        Parameters = parameters;
        SourceObject = sourceObject;
        ReasonCode = reasonCode;
        Detail = detail;
    }

    public bool IsSuccessful { get; }

    public string? Sql { get; }

    public IReadOnlyList<SqlParameterSpec> Parameters { get; }

    /// <summary>Sorgunun okudugu obje. Tek kaynak zorunlulugu nedeniyle tek deger.</summary>
    public string? SourceObject { get; }

    public ReasonCode ReasonCode { get; }

    /// <summary>Ic teshis notu; kullaniciya gosterilmez.</summary>
    public string? Detail { get; }

    public static QueryBuildResult Success(
        string sql,
        IReadOnlyList<SqlParameterSpec> parameters,
        string sourceObject)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceObject);

        return new QueryBuildResult(true, sql, parameters, sourceObject, ReasonCode.None, null);
    }

    public static QueryBuildResult Failure(ReasonCode reasonCode, string detail)
    {
        if (reasonCode == ReasonCode.None)
        {
            throw new ArgumentException("Basarisizlik icin gerekce kodu zorunludur.", nameof(reasonCode));
        }

        return new QueryBuildResult(false, null, [], null, reasonCode, detail);
    }
}
