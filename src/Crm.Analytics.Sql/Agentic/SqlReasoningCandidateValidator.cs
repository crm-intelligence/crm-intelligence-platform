using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.Contracts.V2;
using Crm.Analytics.Sql.Parsing;
using Crm.Analytics.Sql.QueryBuilder;
using Crm.Analytics.Sql.SemanticContext;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Crm.Analytics.Sql.Agentic;

/// <summary>
/// Shared deterministic conversion boundary for every externally or provider-produced,
/// untrusted SQL candidate. A successful result is still not executable until the common
/// guardrail pipeline accepts it.
/// </summary>
internal static class SqlReasoningCandidateValidator
{
    public static QueryBuildResult Validate(
        SqlReasoningResponse? response,
        CanonicalQuery query,
        SemanticQueryContext semanticContext,
        TSqlParserFactory parserFactory)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(semanticContext);
        ArgumentNullException.ThrowIfNull(parserFactory);

        if (response is null)
        {
            return Failure(ReasonCode.GR014, "SQL reasoning client bos response dondurdu.");
        }

        if (!Enum.IsDefined(response.Status))
        {
            return Failure(ReasonCode.GR014, "SQL reasoning response status gecersiz.");
        }

        if (response.Status == SqlReasoningResponseStatus.UnableToResolve)
        {
            return string.IsNullOrWhiteSpace(response.Sql)
                ? Failure(ReasonCode.GR014, "SQL reasoning modeli candidate uretemedi.")
                : Failure(
                    ReasonCode.GR014,
                    "UnableToResolve response SQL tasiyamaz; structured response gecersiz.");
        }

        if (string.IsNullOrWhiteSpace(response.Sql))
        {
            return Failure(ReasonCode.GR014, "Candidate response SQL tasimiyor.");
        }

        var parseResult = parserFactory.Parse(response.Sql);
        if (!parseResult.IsSuccessful)
        {
            return Failure(ReasonCode.GR014, "Candidate SQL yapisal olarak parse edilemedi.");
        }

        var variableCollector = new VariableReferenceCollector();
        parseResult.Fragment!.Accept(variableCollector);
        if (variableCollector.Count > 0)
        {
            return Failure(
                ReasonCode.GR014,
                "Candidate parameter veya @scope* referansi tasiyamaz; " +
                "tum parameter degerleri backend-owned olmalidir.");
        }

        var referencedKeys = response.ReferencedSemanticKeys;
        if (referencedKeys.Length == 0
            || referencedKeys.Length > 64
            || referencedKeys.Any(string.IsNullOrWhiteSpace)
            || referencedKeys.Distinct(StringComparer.Ordinal).Count()
                != referencedKeys.Length)
        {
            return Failure(
                ReasonCode.GR014,
                "Candidate semantic key bildirimi bos, tekrarli veya limit disinda.");
        }

        var allowedSemanticKeys = semanticContext.Metrics.Select(metric => metric.Key)
            .Concat(semanticContext.Dimensions.Select(dimension => dimension.Key))
            .Concat(semanticContext.TimeDimensions.Select(dimension => dimension.Key))
            .ToHashSet(StringComparer.Ordinal);
        if (referencedKeys.Any(key => !allowedSemanticKeys.Contains(key)))
        {
            return Failure(
                ReasonCode.GR014,
                "Candidate minimum semantic context disinda semantic key referansliyor.");
        }

        var requiredSemanticKeys = query.Metrics
            .Concat(query.Dimensions)
            .Concat(query.Filters.Select(filter => filter.Field))
            .Concat(query.Ordering.Select(ordering => ordering.TargetKey))
            .Concat(query.Calculations.SelectMany(calculation => calculation.MetricKeys))
            .ToHashSet(StringComparer.Ordinal);
        if (!requiredSemanticKeys.IsSubsetOf(referencedKeys))
        {
            return Failure(
                ReasonCode.GR014,
                "Candidate canonical intent'in zorunlu semantic key'lerini bildirmiyor.");
        }

        var relationshipIds = response.ReferencedRelationshipIds;
        if (relationshipIds.Length > 32
            || relationshipIds.Any(string.IsNullOrWhiteSpace)
            || relationshipIds.Distinct(StringComparer.OrdinalIgnoreCase).Count()
                != relationshipIds.Length)
        {
            return Failure(
                ReasonCode.GR006,
                "Candidate relationship bildirimi tekrarli veya limit disinda.");
        }

        var approvedRelationshipIds = semanticContext.Relationships
            .Select(relationship => relationship.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (relationshipIds.Any(id => !approvedRelationshipIds.Contains(id)))
        {
            return Failure(
                ReasonCode.GR006,
                "Candidate approved semantic context disinda relationship referansliyor.");
        }

        if (!semanticContext.Capabilities.JoinExecutionEnabled
            && relationshipIds.Length > 0)
        {
            return Failure(ReasonCode.GR006, "Runtime join execution capability kapali.");
        }

        return QueryBuildResult.Success(
            response.Sql,
            [],
            semanticContext.Sources[0].LogicalSource);
    }

    private static QueryBuildResult Failure(ReasonCode reasonCode, string detail) =>
        QueryBuildResult.Failure(reasonCode, detail);

    private sealed class VariableReferenceCollector : TSqlFragmentVisitor
    {
        public int Count { get; private set; }

        public override void Visit(VariableReference node) => Count++;
    }
}
