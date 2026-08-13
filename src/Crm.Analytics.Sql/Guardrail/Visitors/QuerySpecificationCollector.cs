using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Crm.Analytics.Sql.Guardrail.Visitors;

/// <summary>
/// Agactaki <b>tum</b> <see cref="QuerySpecification"/> dugumlerini toplar.
/// </summary>
/// <remarks>
/// <para>
/// Kapsam filtresi enjeksiyonunun dogrulugu tamamen buna baglidir. "Tek bir WHERE vardir"
/// varsayimi su yapilarda coker ve her birinde filtresiz veri sizabilir:
/// </para>
/// <list type="bullet">
/// <item><description><b>CTE:</b> <c>WITH x AS (SELECT SUM(net_amount) FROM vw_sales) SELECT * FROM x</c>
/// — dis sorguda region kolonu bile yoktur, filtre yalnizca dis WHERE'e eklenirse CTE tum
/// bolgeleri okur ve toplar.</description></item>
/// <item><description><b>Turetilmis tablo:</b> ayni sorun alt sorgu icin gecerlidir.</description></item>
/// <item><description><b>UNION:</b> <see cref="BinaryQueryExpression"/> iki ayri
/// QuerySpecification uretir; yalnizca birine filtre eklemek digerini serbest birakir.</description></item>
/// <item><description><b>WHERE icindeki alt sorgu:</b> <c>WHERE customer_id IN (SELECT ...)</c></description></item>
/// </list>
/// <para>
/// <see cref="TSqlFragmentVisitor"/> agacin tamamini gezdigi icin bu yapilarin hepsi
/// otomatik olarak kapsanir; ozel durum listesi tutmuyoruz.
/// </para>
/// </remarks>
public sealed class QuerySpecificationCollector : TSqlFragmentVisitor
{
    private readonly List<QuerySpecification> found = [];

    public IReadOnlyList<QuerySpecification> Found => found;

    public override void Visit(QuerySpecification node) => found.Add(node);

    /// <summary>Verilen agactaki tum sorgu bloklarini dondurur.</summary>
    public static IReadOnlyList<QuerySpecification> CollectFrom(TSqlFragment fragment)
    {
        ArgumentNullException.ThrowIfNull(fragment);

        var collector = new QuerySpecificationCollector();
        fragment.Accept(collector);
        return collector.Found;
    }
}
