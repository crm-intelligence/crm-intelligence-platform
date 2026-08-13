using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Crm.Analytics.Sql.Guardrail.Visitors;

/// <summary>
/// Agactaki tum tablo ve kolon referanslarini, ayrica sorgunun kendi urettigi kolon adlarini
/// toplar.
/// </summary>
/// <remarks>
/// <para>
/// Turetilmis adlarin ayri tutulmasi zorunludur: <c>SUM(price) AS tutar</c> ifadesindeki
/// <c>tutar</c> veritabaninda bir kolon DEGILDIR. Allow-list'te aranirsa mesru bir sorgu
/// "izinsiz kolon" olarak reddedilirdi. Ayni durum CTE ve turetilmis tablo kolonlari icin de
/// gecerlidir.
/// </para>
/// <para>
/// Kolonlar SELECT listesiyle sinirli DEGIL, agacin tamamindan toplanir: WHERE, GROUP BY,
/// ORDER BY, HAVING ve JOIN ON dahil. PII'nin WHERE uzerinden sizdirilmasi
/// (<c>WHERE email LIKE 'ahmet%'</c>) tam olarak bu yuzden mumkundu.
/// </para>
/// </remarks>
public sealed class TableAndColumnCollector : TSqlFragmentVisitor
{
    private readonly List<TableReferenceInfo> tables = [];
    private readonly List<ColumnReferenceExpression> columns = [];
    private readonly HashSet<string> derivedNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> qualifierToObject = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Tum tablo referanslari (tam ad ile).</summary>
    public IReadOnlyList<TableReferenceInfo> Tables => tables;

    /// <summary>Agacin her yerinden toplanan kolon referanslari.</summary>
    public IReadOnlyList<ColumnReferenceExpression> Columns => columns;

    /// <summary>
    /// Sorgunun kendi urettigi adlar: SELECT alias'lari, CTE kolonlari, turetilmis tablo
    /// kolonlari. Bunlar allow-list'te aranmaz.
    /// </summary>
    public IReadOnlySet<string> DerivedNames => derivedNames;

    /// <summary>Nitelendirici (alias veya tablo adi) -> obje tam adi.</summary>
    public IReadOnlyDictionary<string, string> QualifierToObject => qualifierToObject;

    public override void Visit(NamedTableReference node)
    {
        var objectName = SchemaObjectNames.FullName(node.SchemaObject);
        var qualifier = node.Alias ?? node.SchemaObject.Identifiers[^1];

        tables.Add(new TableReferenceInfo(objectName, qualifier, node));

        // Ayni alias birden fazla objeye baglanamaz; ilk kayit korunur. Cakisan alias
        // kullanan bir sorgu zaten T-SQL tarafinda gecersizdir.
        qualifierToObject.TryAdd(qualifier.Value, objectName);
    }

    public override void Visit(ColumnReferenceExpression node) => columns.Add(node);

    public override void Visit(SelectScalarExpression node)
    {
        if (!string.IsNullOrWhiteSpace(node.ColumnName?.Value))
        {
            derivedNames.Add(node.ColumnName.Value);
        }
    }

    public override void Visit(CommonTableExpression node)
    {
        // CTE adi bir obje degildir; allow-list'te aranmamasi icin turetilmis ad sayilir.
        if (!string.IsNullOrWhiteSpace(node.ExpressionName?.Value))
        {
            derivedNames.Add(node.ExpressionName.Value);
        }

        foreach (var column in node.Columns.Where(column => !string.IsNullOrWhiteSpace(column.Value)))
        {
            derivedNames.Add(column.Value);
        }
    }

    public override void Visit(QueryDerivedTable node)
    {
        if (!string.IsNullOrWhiteSpace(node.Alias?.Value))
        {
            derivedNames.Add(node.Alias.Value);
        }

        foreach (var column in node.Columns.Where(column => !string.IsNullOrWhiteSpace(column.Value)))
        {
            derivedNames.Add(column.Value);
        }
    }

    public static TableAndColumnCollector CollectFrom(TSqlFragment fragment)
    {
        ArgumentNullException.ThrowIfNull(fragment);

        var collector = new TableAndColumnCollector();
        fragment.Accept(collector);
        return collector;
    }
}
