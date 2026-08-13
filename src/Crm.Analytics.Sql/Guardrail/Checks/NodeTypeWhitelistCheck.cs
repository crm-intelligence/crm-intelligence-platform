using Crm.Analytics.Sql.Catalog;
using Crm.Analytics.Sql.Contracts;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Crm.Analytics.Sql.Guardrail.Checks;

/// <summary>
/// Kontrol 5: veri kaynagi olabilecek AST dugumlerini <b>beyaz listeye</b> gore denetler.
/// </summary>
/// <remarks>
/// <para>
/// Bu kontrolun varlik sebebi test ile kanitlandi: <c>SELECT v.region FROM vw_sales v
/// CROSS APPLY dbo.fn_gizli(v.region) AS f</c> sorgusu diger tum kontrolleri geciyordu.
/// Gramere uygun bir SELECT oldugu icin SelectOnly'ye takilmiyor;
/// <see cref="GuardrailCheckName.AllowListObjects"/> yalnizca <see cref="NamedTableReference"/>
/// denetledigi icin fonksiyon cagrisi allow-list'te hic aranmiyordu. Fonksiyonun govdesi
/// allow-list disi bir tabloya erisebilir.
/// </para>
/// <para>
/// Kara liste degil beyaz liste kullanilir: kara liste, her yeni T-SQL surumunde yeni bir
/// yapinin sessizce gecmesi anlamina gelirdi.
/// </para>
/// </remarks>
public sealed class NodeTypeWhitelistCheck : IGuardrailCheck
{
    public GuardrailCheckName Name => GuardrailCheckName.NodeTypeWhitelist;

    public CheckResult Execute(GuardrailContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var visitor = new ForbiddenNodeVisitor(context.AllowList);
        context.RequireFragment().Accept(visitor);

        if (visitor.Violations.Count > 0)
        {
            return CheckResult.Fail(Name, ReasonCode.GR011,
                "Desteklenmeyen yapi(lar): " + string.Join("; ", visitor.Violations.Take(3)));
        }

        return CheckResult.Pass(Name);
    }

    private sealed class ForbiddenNodeVisitor(AllowListDocument allowList) : TSqlFragmentVisitor
    {
        private readonly List<string> violations = [];

        public IReadOnlyList<string> Violations => violations;

        /// <summary>
        /// Tum tablo referanslari buraya duser. Yalnizca acikca izin verilen tipler gecer;
        /// taninmayan her tip reddedilir (OPENROWSET, OPENJSON, OPENQUERY, inline TVF,
        /// PIVOT/UNPIVOT, tablo degiskeni, CHANGETABLE ...).
        /// </summary>
        public override void Visit(TableReference node)
        {
            switch (node)
            {
                case NamedTableReference:
                case QueryDerivedTable:
                case QualifiedJoin:
                case JoinParenthesisTableReference:
                    break;

                case UnqualifiedJoin unqualifiedJoin:
                    // APPLY, ScriptDom'da UnqualifiedJoin olarak temsil edilir. Virgullu
                    // eski stil JOIN'e izin verilir (sayimi JoinPathAllowed yapar), ancak
                    // CROSS/OUTER APPLY bir fonksiyon cagrisi kapisidir ve reddedilir.
                    if (unqualifiedJoin.UnqualifiedJoinType is not UnqualifiedJoinType.CrossJoin)
                    {
                        violations.Add($"{unqualifiedJoin.UnqualifiedJoinType} desteklenmiyor");
                    }

                    break;

                default:
                    violations.Add($"{node.GetType().Name} desteklenmiyor");
                    break;
            }
        }

        /// <summary>
        /// Fonksiyon cagrilari beyaz listeye gore denetlenir. Sema/sunucu bilgisi sizdiran
        /// fonksiyonlar (DB_NAME, SUSER_NAME, OBJECT_NAME ...) listede yer almadigi icin
        /// reddedilir.
        /// </summary>
        public override void Visit(FunctionCall node)
        {
            var functionName = node.FunctionName?.Value;

            if (string.IsNullOrWhiteSpace(functionName))
            {
                violations.Add("Adsiz fonksiyon cagrisi");
                return;
            }

            if (!allowList.IsAllowedFunction(functionName))
            {
                violations.Add($"Izinli olmayan fonksiyon: {functionName}");
            }
        }
    }
}
