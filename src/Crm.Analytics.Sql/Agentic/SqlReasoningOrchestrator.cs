using System.Collections.Immutable;
using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.Contracts.V2;
using Crm.Analytics.Sql.Parsing;
using Crm.Analytics.Sql.QueryBuilder;
using Crm.Analytics.Sql.Routing;
using Crm.Analytics.Sql.SemanticContext;

namespace Crm.Analytics.Sql.Agentic;

/// <summary>
/// Resolves minimum approved semantic context and requests exactly one untrusted SQL
/// candidate. It does not authorize, mutate scope, validate SQL security or execute SQL.
/// </summary>
internal sealed class SqlReasoningOrchestrator(
    DataSource runtime,
    ISqlSemanticTools semanticTools,
    ISqlReasoningModelClient modelClient,
    TSqlParserFactory parserFactory)
{
    private static readonly ImmutableArray<SqlSemanticToolKind> AvailableTools =
        Enum.GetValues<SqlSemanticToolKind>().ToImmutableArray();

    public async Task<QueryBuildResult> GenerateAsync(
        CanonicalQuery query,
        QueryStrategyDecision decision,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(decision);
        cancellationToken.ThrowIfCancellationRequested();

        if (decision.Outcome != QueryCapabilityOutcome.AgenticRequired)
        {
            return Failure(
                ReasonCode.GR014,
                "SQL reasoning orchestration yalnizca AgenticRequired karariyla baslatilabilir.");
        }

        if (runtime != DataSource.Dwh)
        {
            return Failure(
                ReasonCode.GR014,
                "Phase 4A agentic candidate scope yalnizca DWH runtime'ini destekler.");
        }

        var semanticResult = semanticTools.GetQuerySemanticContext(runtime, query);
        if (!semanticResult.IsSuccessful)
        {
            return Failure(
                ReasonCode.GR014,
                $"Approved semantic context olusturulamadi: {semanticResult.Failure!.Code}.");
        }

        var semanticContext = semanticResult.Value!;
        if (semanticContext.Sources.Length != 1)
        {
            return Failure(
                ReasonCode.GR006,
                "Phase 4A agentic candidate tam olarak bir approved physical source gerektirir.");
        }

        var request = new SqlReasoningRequest(
            query,
            semanticContext,
            new SqlReasoningAllowedCapabilities(
                decision.Complexity,
                decision.Features.ToImmutableArray(),
                decision.Reasons.ToImmutableArray(),
                semanticContext.Capabilities.MaximumExecutableJoins,
                semanticContext.Capabilities.JoinExecutionEnabled),
            new SqlGenerationConstraints(
                SelectOnly: true,
                SingleStatementOnly: true,
                ExplicitProjectionRequired: true,
                ApprovedObjectsAndColumnsOnly: true,
                BackendOwnsLiteralParameterization: true,
                BackendOwnsDataScope: true,
                CandidateMustNotExecute: true,
                MaximumCandidates: 1),
            AvailableTools);

        SqlReasoningResponse response;
        try
        {
            response = await modelClient.GenerateAsync(request, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Failure(
                ReasonCode.GR014,
                $"SQL reasoning model invocation basarisiz oldu: {exception.GetType().Name}.");
        }

        return SqlReasoningCandidateValidator.Validate(
            response, query, semanticContext, parserFactory);
    }

    private static QueryBuildResult Failure(ReasonCode reasonCode, string detail) =>
        QueryBuildResult.Failure(reasonCode, detail);
}
