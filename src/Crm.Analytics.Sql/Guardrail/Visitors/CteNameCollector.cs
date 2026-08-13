using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Crm.Analytics.Sql.Guardrail.Visitors;

/// <summary>
/// <c>WITH</c> ile tanimlanan CTE adlarini toplar.
/// </summary>
/// <remarks>
/// Bu adlar veritabaninda bir obje DEGILDIR; allow-list'te aranmamalari gerekir. Aksi halde
/// mesru bir CTE kullanimi "allow-list disi tablo" olarak reddedilirdi. Ayni sekilde bir CTE
/// referansina kapsam filtresi eklenmez — filtre, CTE'nin <b>govdesindeki</b> sorgu blokuna
/// eklenir ve orasi <see cref="QuerySpecificationCollector"/> tarafindan zaten bulunur.
/// </remarks>
public sealed class CteNameCollector : TSqlFragmentVisitor
{
    private readonly HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlySet<string> Names => names;

    public override void Visit(CommonTableExpression node)
    {
        if (!string.IsNullOrWhiteSpace(node.ExpressionName?.Value))
        {
            names.Add(node.ExpressionName.Value);
        }
    }

    public static IReadOnlySet<string> CollectFrom(TSqlFragment fragment)
    {
        ArgumentNullException.ThrowIfNull(fragment);

        var collector = new CteNameCollector();
        fragment.Accept(collector);
        return collector.Names;
    }
}
