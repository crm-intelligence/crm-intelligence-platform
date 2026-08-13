using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Crm.Analytics.Sql.Guardrail.Visitors;

/// <summary>
/// Sorgu yapisi hakkinda ortak sorular.
/// </summary>
public static class QueryStructure
{
    /// <summary>
    /// Kullaniciya donen sonucu ureten sorgu bloklari.
    /// </summary>
    /// <remarks>
    /// Bu ayrim iki kontrol icin de zorunludur:
    /// <list type="bullet">
    /// <item><description><c>MinCellSize</c>: alt sorgu bir filtredir, kirilim degil.
    /// Ayrim yapilmazsa <c>WHERE customer_id IN (SELECT customer_id ...)</c> gibi mesru
    /// filtreler reddedilir.</description></item>
    /// <item><description><c>RowLimit</c>: alt sorguya <c>TOP</c> eklemek sonucu BOZAR
    /// (agregasyon kismi veri uzerinden hesaplanir). Limit yalnizca disa cikan bloga
    /// eklenebilir.</description></item>
    /// </list>
    /// UNION durumunda her kol ayri ayri disa ciktigi icin hepsi dahil edilir.
    /// </remarks>
    public static HashSet<QuerySpecification> OutermostBlocks(TSqlFragment fragment)
    {
        ArgumentNullException.ThrowIfNull(fragment);

        var blocks = new HashSet<QuerySpecification>();

        if (fragment is not TSqlScript script)
        {
            return blocks;
        }

        var selectStatements = script.Batches
            .SelectMany(batch => batch.Statements)
            .OfType<SelectStatement>();

        foreach (var statement in selectStatements)
        {
            Add(statement.QueryExpression, blocks);
        }

        return blocks;
    }

    private static void Add(QueryExpression? expression, HashSet<QuerySpecification> blocks)
    {
        switch (expression)
        {
            case QuerySpecification specification:
                blocks.Add(specification);
                break;

            case BinaryQueryExpression binary:
                Add(binary.FirstQueryExpression, blocks);
                Add(binary.SecondQueryExpression, blocks);
                break;

            case QueryParenthesisExpression parenthesis:
                Add(parenthesis.QueryExpression, blocks);
                break;
        }
    }
}
