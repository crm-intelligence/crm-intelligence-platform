using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.Guardrail.Visitors;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Crm.Analytics.Sql.Guardrail.Checks;

/// <summary>
/// Kontrol 9: JOIN yollari allow-list'te tanimli olmali ve sayisi <c>maxJoins</c>'i asmamali.
/// </summary>
/// <remarks>
/// <para>
/// Iki objenin ayri ayri izinli olmasi, birlikte raporlanmalarinin da izinli oldugu anlamina
/// gelmez: bir JOIN, tek basina zararsiz iki veri kumesini birlestirerek yeni bir ifsa
/// yaratabilir. Bu yuzden yol ayrica tanimli olmak zorunda.
/// </para>
/// <para>
/// Virgullu eski stil yazim (<c>FROM a, b</c>) da sayilir ve denetlenir; JOIN anahtar
/// kelimesi aramak bu bicimi tamamen kacirirdi.
/// </para>
/// <para>
/// CTE ve turetilmis tablo referanslari yol denetiminden muaftir: govdeleri allow-list'e
/// karsi zaten dogrulanmis ve kapsam filtresi almistir, yani yeni bir veri kaynagi acmazlar.
/// JOIN SAYISI ise onlari da kapsar — maliyet siniri yapiya bakmaz.
/// </para>
/// </remarks>
public sealed class JoinPathAllowedCheck : IGuardrailCheck
{
    public GuardrailCheckName Name => GuardrailCheckName.JoinPathAllowed;

    public CheckResult Execute(GuardrailContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var fragment = context.RequireFragment();
        var collector = new JoinCollector();
        fragment.Accept(collector);

        if (collector.Joins.Count > context.AllowList.MaxJoins)
        {
            return CheckResult.Fail(Name, ReasonCode.GR006,
                $"JOIN sayisi {collector.Joins.Count}, sinir {context.AllowList.MaxJoins}.");
        }

        var derivedNames = TableAndColumnCollector.CollectFrom(fragment).DerivedNames;

        foreach (var join in collector.Joins)
        {
            var failure = ValidatePath(context, join, derivedNames);

            if (failure is not null)
            {
                return failure;
            }
        }

        return CheckResult.Pass(Name);
    }

    private CheckResult? ValidatePath(
        GuardrailContext context,
        (TableReference Left, TableReference Right) join,
        IReadOnlySet<string> derivedNames)
    {
        var leftObjects = RealObjectsUnder(join.Left, derivedNames);
        var rightObjects = RealObjectsUnder(join.Right, derivedNames);

        // Taraflardan biri yalnizca CTE / turetilmis tablo iceriyorsa yol denetimi yapilmaz.
        if (leftObjects.Count == 0 || rightObjects.Count == 0)
        {
            return null;
        }

        var pathExists = leftObjects.Any(left =>
            rightObjects.Any(right => HasDefinedPath(context, left, right)));

        if (!pathExists)
        {
            return CheckResult.Fail(Name, ReasonCode.GR006,
                $"Tanimsiz JOIN yolu: '{string.Join(",", leftObjects)}' <-> '{string.Join(",", rightObjects)}'.");
        }

        return null;
    }

    private static bool HasDefinedPath(GuardrailContext context, string left, string right)
    {
        var logicalLeft = context.AllowList.FindLogicalNameBySqlObject(left);
        var logicalRight = context.AllowList.FindLogicalNameBySqlObject(right);
        if (logicalLeft is null || logicalRight is null)
        {
            return false;
        }

        // Yon guvenlik acisindan anlam tasimaz: A->B tanimliysa B->A de gecerlidir.
        var forward = context.AllowList.FindObject(logicalLeft)?.JoinPaths
            .Any(path => path.Enabled
                && path.To.Equals(logicalRight, StringComparison.OrdinalIgnoreCase)) == true;

        var backward = context.AllowList.FindObject(logicalRight)?.JoinPaths
            .Any(path => path.Enabled
                && path.To.Equals(logicalLeft, StringComparison.OrdinalIgnoreCase)) == true;

        return forward || backward;
    }

    /// <summary>
    /// Verilen referansin altindaki gercek obje adlari. Turetilmis tablolarin icine
    /// GIRILMEZ; onlarin govdesi ayri bir sorgu blogudur.
    /// </summary>
    private static List<string> RealObjectsUnder(TableReference reference, IReadOnlySet<string> derivedNames)
    {
        var found = new List<string>();
        Walk(reference, found, derivedNames);
        return found;
    }

    private static void Walk(TableReference reference, List<string> found, IReadOnlySet<string> derivedNames)
    {
        switch (reference)
        {
            case NamedTableReference named:
                var objectName = SchemaObjectNames.FullName(named.SchemaObject);

                // CTE referansi bir obje degildir.
                if (!derivedNames.Contains(objectName))
                {
                    found.Add(objectName);
                }

                break;

            case QualifiedJoin qualified:
                Walk(qualified.FirstTableReference, found, derivedNames);
                Walk(qualified.SecondTableReference, found, derivedNames);
                break;

            case UnqualifiedJoin unqualified:
                Walk(unqualified.FirstTableReference, found, derivedNames);
                Walk(unqualified.SecondTableReference, found, derivedNames);
                break;

            case JoinParenthesisTableReference parenthesis:
                Walk(parenthesis.Join, found, derivedNames);
                break;

            // QueryDerivedTable ve taninmayan tipler: icine girilmez.
            // Taninmayan tipler NodeTypeWhitelist tarafindan zaten reddedilir.
            default:
                break;
        }
    }

    private sealed class JoinCollector : TSqlFragmentVisitor
    {
        private readonly List<(TableReference Left, TableReference Right)> joins = [];

        public IReadOnlyList<(TableReference Left, TableReference Right)> Joins => joins;

        public override void Visit(QualifiedJoin node) =>
            joins.Add((node.FirstTableReference, node.SecondTableReference));

        public override void Visit(UnqualifiedJoin node) =>
            joins.Add((node.FirstTableReference, node.SecondTableReference));

        /// <summary>
        /// Virgullu eski stil yazim (<c>FROM a, b</c>) bir JOIN DUGUMU uretmez; ScriptDom
        /// bunu <see cref="FromClause.TableReferences"/> icinde birden fazla eleman olarak
        /// temsil eder. Yalnizca join dugumlerini saymak bu bicimi tamamen kacirirdi —
        /// test bunu ortaya cikardi.
        /// </summary>
        public override void Visit(FromClause node)
        {
            for (var index = 1; index < node.TableReferences.Count; index++)
            {
                joins.Add((node.TableReferences[index - 1], node.TableReferences[index]));
            }
        }
    }
}
