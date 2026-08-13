using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.Guardrail.Visitors;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Crm.Analytics.Sql.Guardrail.Checks;

/// <summary>
/// Kontrol 10: kimlik seviyesinde detayi ve tek kaydi izole eden sorgulari reddeder.
/// </summary>
/// <remarks>
/// <para>
/// Bu kontrol, proje dokumanindaki 12 kontrolun kacirdigi bir sizinti sinifini kapatir:
/// <c>SELECT customer_id, SUM(net_amount) ... GROUP BY customer_id</c> sorgusu allow-list'te,
/// PII degil, kapsam icinde ve tum sozdizimsel kontrolleri gecer — ama tek bir kisinin
/// harcamasini aciga cikarir. Guardrail'in geri kalani sozdizimsel denetim yapar; bu kontrol
/// <b>sonucun ne kadar ince taneli oldugunu</b> denetler.
/// </para>
/// <para>
/// Iki farkli yol kapatilir:
/// </para>
/// <list type="number">
/// <item><description><b>Kimlik-grain kirilimi:</b> kimlik kolonunun SELECT listesinde veya
/// GROUP BY'da kullanilmasi. Agregasyon <b>icinde</b> kullanim serbesttir —
/// <c>COUNT(DISTINCT order_id)</c> mesru bir metriktir ve kimlik ifsa etmez. Filtrede
/// kullanim da serbesttir: filtre bir kirilim degildir.</description></item>
/// <item><description><b>Kucuk grup secimi:</b> <c>HAVING COUNT(*) = 1</c> gibi ifadelerle
/// kucuk gruplari ayiklamak, tek kisiyi tespit etmenin klasik yoludur (differencing attack).
/// </description></item>
/// </list>
/// </remarks>
public sealed class MinCellSizeCheck : IGuardrailCheck
{
    private static readonly string[] AggregateFunctions =
        ["SUM", "COUNT", "COUNT_BIG", "AVG", "MIN", "MAX"];

    public GuardrailCheckName Name => GuardrailCheckName.MinCellSize;

    public CheckResult Execute(GuardrailContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var fragment = context.RequireFragment();
        var outermostBlocks = QueryStructure.OutermostBlocks(fragment);

        foreach (var block in QuerySpecificationCollector.CollectFrom(fragment))
        {
            // SELECT listesi yalnizca EN DIS blokta denetlenir: kullaniciya donen sonuc odur.
            // Alt sorgular ara sonuctur ve disa cikmaz —
            // 'WHERE customer_id IN (SELECT customer_id FROM vw_customer)' bir filtredir,
            // kirilim degil. Bu ayrimi yapmadan mesru filtreler reddediliyordu.
            var failure = (outermostBlocks.Contains(block) ? ValidateSelectElements(context, block) : null)
                // GROUP BY ve HAVING TUM bloklarda denetlenir: kimlik kirilimi bir CTE icinde
                // yapilip dis sorguya tasinabilir, o kacis yolu acik kalmamali.
                ?? ValidateGroupBy(context, block)
                ?? ValidateHaving(context, block);

            if (failure is not null)
            {
                return failure;
            }
        }

        return CheckResult.Pass(Name);
    }

    /// <summary>
    /// Kullaniciya donen sonucu ureten sorgu bloklari. UNION durumunda her kol ayri ayri
    /// disa cikar, bu yuzden hepsi dahil edilir.
    /// </summary>
    private CheckResult? ValidateSelectElements(GuardrailContext context, QuerySpecification block)
    {
        if (context.AllowList.AllowIdentityDetail)
        {
            return null;
        }

        foreach (var element in block.SelectElements.OfType<SelectScalarExpression>())
        {
            // Agregasyon iceren ifade bir kirilim degildir; icindeki kimlik kolonu ifsa yaratmaz.
            if (ContainsAggregate(element.Expression))
            {
                continue;
            }

            var identity = FindIdentityColumn(context, element.Expression);

            if (identity is not null)
            {
                return CheckResult.Fail(Name, ReasonCode.GR012,
                    $"Kimlik kolonu SELECT listesinde kirilim olarak kullanilmis: '{identity}'.");
            }
        }

        return null;
    }

    private CheckResult? ValidateGroupBy(GuardrailContext context, QuerySpecification block)
    {
        if (block.GroupByClause is null)
        {
            return null;
        }

        var identity = FindIdentityColumn(context, block.GroupByClause);

        if (identity is not null)
        {
            return CheckResult.Fail(Name, ReasonCode.GR012,
                $"Kimlik kolonu GROUP BY'da kullanilmis: '{identity}'.");
        }

        return null;
    }

    /// <summary>
    /// <c>HAVING COUNT(*) &lt; n</c> bicimindeki kucuk grup secimlerini reddeder.
    /// Buyuk gruplari secmek (<c>&gt;= 10</c>) serbesttir.
    /// </summary>
    private CheckResult? ValidateHaving(GuardrailContext context, QuerySpecification block)
    {
        if (block.HavingClause is null)
        {
            return null;
        }

        var comparisons = new ComparisonCollector();
        block.HavingClause.Accept(comparisons);

        foreach (var comparison in comparisons.Found)
        {
            if (!TryReadCountThreshold(comparison, out var threshold, out var comparisonType))
            {
                continue;
            }

            var selectsSmallGroups = comparisonType switch
            {
                BooleanComparisonType.Equals => threshold < context.AllowList.MinCellSize,
                BooleanComparisonType.LessThan => threshold <= context.AllowList.MinCellSize,
                BooleanComparisonType.LessThanOrEqualTo => threshold < context.AllowList.MinCellSize,
                _ => false
            };

            if (selectsSmallGroups)
            {
                return CheckResult.Fail(Name, ReasonCode.GR012,
                    $"HAVING ile minimum grup buyuklugunun altinda secim: {comparisonType} {threshold}, " +
                    $"minCellSize {context.AllowList.MinCellSize}.");
            }
        }

        return null;
    }

    private static bool TryReadCountThreshold(
        BooleanComparisonExpression comparison,
        out int threshold,
        out BooleanComparisonType comparisonType)
    {
        threshold = 0;
        comparisonType = comparison.ComparisonType;

        // COUNT(...) <op> <sayi> veya <sayi> <op> COUNT(...) bicimleri.
        if (IsCountCall(comparison.FirstExpression)
            && comparison.SecondExpression is IntegerLiteral right
            && int.TryParse(right.Value, out threshold))
        {
            return true;
        }

        if (IsCountCall(comparison.SecondExpression)
            && comparison.FirstExpression is IntegerLiteral left
            && int.TryParse(left.Value, out threshold))
        {
            // Taraflar yer degistirdiginde karsilastirma yonu de tersine cevrilir.
            comparisonType = comparisonType switch
            {
                BooleanComparisonType.LessThan => BooleanComparisonType.GreaterThan,
                BooleanComparisonType.LessThanOrEqualTo => BooleanComparisonType.GreaterThanOrEqualTo,
                BooleanComparisonType.GreaterThan => BooleanComparisonType.LessThan,
                BooleanComparisonType.GreaterThanOrEqualTo => BooleanComparisonType.LessThanOrEqualTo,
                _ => comparisonType
            };

            return true;
        }

        return false;
    }

    private static bool IsCountCall(ScalarExpression expression) =>
        expression is FunctionCall call
        && call.FunctionName?.Value is { } name
        && (name.Equals("COUNT", StringComparison.OrdinalIgnoreCase)
            || name.Equals("COUNT_BIG", StringComparison.OrdinalIgnoreCase));

    private static bool ContainsAggregate(TSqlFragment fragment)
    {
        var visitor = new AggregateDetector();
        fragment.Accept(visitor);
        return visitor.Found;
    }

    private static string? FindIdentityColumn(GuardrailContext context, TSqlFragment fragment)
    {
        var visitor = new ColumnNameCollector();
        fragment.Accept(visitor);

        return visitor.Names.FirstOrDefault(context.AllowList.IsIdentityColumn);
    }

    private sealed class AggregateDetector : TSqlFragmentVisitor
    {
        public bool Found { get; private set; }

        public override void Visit(FunctionCall node)
        {
            if (node.FunctionName?.Value is { } name
                && AggregateFunctions.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                Found = true;
            }
        }
    }

    private sealed class ColumnNameCollector : TSqlFragmentVisitor
    {
        private readonly List<string> names = [];

        public IReadOnlyList<string> Names => names;

        public override void Visit(ColumnReferenceExpression node)
        {
            var identifiers = node.MultiPartIdentifier?.Identifiers;

            if (identifiers is { Count: > 0 })
            {
                names.Add(identifiers[^1].Value);
            }
        }
    }

    private sealed class ComparisonCollector : TSqlFragmentVisitor
    {
        private readonly List<BooleanComparisonExpression> found = [];

        public IReadOnlyList<BooleanComparisonExpression> Found => found;

        public override void Visit(BooleanComparisonExpression node) => found.Add(node);
    }
}
