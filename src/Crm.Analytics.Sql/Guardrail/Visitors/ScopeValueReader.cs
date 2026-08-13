using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Crm.Analytics.Sql.Guardrail.Visitors;

/// <summary>
/// Sorguda kapsam kolonuna karsi kullanilan sabit degerleri okur.
/// </summary>
/// <remarks>
/// <para>
/// Amac, kullanicinin <b>yetkisi olmayan</b> bir bolgeyi acikca talep ettigini tespit etmek
/// (<c>GR008</c>). Kapsam filtresi zaten enjekte edildigi icin veri sizmaz — ancak kullaniciya
/// "yetkiniz yok" yerine "veri bulunamadi" gibi gorunmesi hem yanlis hem de denetim acisindan
/// zayiftir: yetkisiz erisim denemesi audit'te gorunmelidir.
/// </para>
/// <para>
/// AST uzerinden okunur ve Query Builder ciktisinda calisir.
/// Yalnizca sabit degerler (<see cref="StringLiteral"/>) dikkate alinir; parametre veya
/// ifade ile yazilmis kosullar degerlendirilemez ve sessizce gecirilir.
/// </para>
/// </remarks>
public sealed class ScopeValueReader(string scopeColumn) : TSqlFragmentVisitor
{
    private readonly List<string> requestedValues = [];

    public IReadOnlyList<string> RequestedValues => requestedValues;

    public override void Visit(BooleanComparisonExpression node)
    {
        // Yalnizca esitlik anlamli: 'state <> X' veya 'state > X' bir kapsam talebi degildir.
        if (node.ComparisonType != BooleanComparisonType.Equals)
        {
            return;
        }

        if (IsScopeColumn(node.FirstExpression) && node.SecondExpression is StringLiteral right)
        {
            requestedValues.Add(right.Value);
        }
        else if (IsScopeColumn(node.SecondExpression) && node.FirstExpression is StringLiteral left)
        {
            requestedValues.Add(left.Value);
        }
    }

    public override void Visit(InPredicate node)
    {
        // NOT IN bir kapsam talebi degil, dislama ifadesidir.
        if (node.NotDefined || !IsScopeColumn(node.Expression))
        {
            return;
        }

        foreach (var literal in node.Values.OfType<StringLiteral>())
        {
            requestedValues.Add(literal.Value);
        }
    }

    private bool IsScopeColumn(ScalarExpression? expression)
    {
        if (expression is not ColumnReferenceExpression column)
        {
            return false;
        }

        var identifiers = column.MultiPartIdentifier?.Identifiers;

        return identifiers is { Count: > 0 }
            && identifiers[^1].Value.Equals(scopeColumn, StringComparison.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<string> ReadFrom(TSqlFragment fragment, string scopeColumn)
    {
        ArgumentNullException.ThrowIfNull(fragment);
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeColumn);

        var reader = new ScopeValueReader(scopeColumn);
        fragment.Accept(reader);
        return reader.RequestedValues;
    }
}
