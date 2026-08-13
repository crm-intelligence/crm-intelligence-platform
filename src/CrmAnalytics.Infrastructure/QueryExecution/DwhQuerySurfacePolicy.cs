using Crm.Analytics.Sql.Catalog;
using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.Guardrail.Visitors;
using Crm.Analytics.Sql.Parsing;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace CrmAnalytics.Infrastructure.QueryExecution;

/// <summary>
/// Fail-closed DWH execution surface. The same embedded, versioned contract used by
/// Query Builder supplies the exact physical MART view names accepted here.
/// </summary>
internal static class DwhQuerySurfacePolicy
{
    private static readonly TSqlParserFactory ParserFactory = new();
    private static readonly AllowListDocument Contract =
        AllowListLoader.FromJson(ContractResources.ReadOlistAllowList());

    internal static bool Allows(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return false;
        }

        var parsed = ParserFactory.Parse(sql);
        if (!parsed.IsSuccessful || parsed.Fragment is not TSqlScript script)
        {
            return false;
        }

        var statements = script.Batches
            .SelectMany(batch => batch.Statements)
            .ToArray();
        if (statements is not [SelectStatement select]
            || select.Into is not null)
        {
            return false;
        }

        var visitor = new SurfaceVisitor();
        script.Accept(visitor);
        return visitor.NamedObjectCount > 0 && !visitor.HasViolation;
    }

    private sealed class SurfaceVisitor : TSqlFragmentVisitor
    {
        public int NamedObjectCount { get; private set; }
        public bool HasViolation { get; private set; }

        public override void Visit(TableReference node)
        {
            if (node is not (NamedTableReference
                or QueryDerivedTable
                or QualifiedJoin
                or JoinParenthesisTableReference))
            {
                HasViolation = true;
            }
        }

        public override void Visit(NamedTableReference node)
        {
            NamedObjectCount++;
            var objectName = SchemaObjectNames.FullName(node.SchemaObject);
            if (!Contract.IsAllowedExecutionObject(objectName))
            {
                HasViolation = true;
            }
        }
    }
}
