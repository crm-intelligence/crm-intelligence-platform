using System.Collections.Immutable;
using Crm.Analytics.Sql.Contracts.V2;
using Crm.Analytics.Sql.Routing;
using Crm.Analytics.Sql.SemanticContext;

namespace Crm.Analytics.Sql.Agentic;

/// <summary>
/// Provider-independent, structured input for one SQL reasoning invocation. It contains the
/// internal analytical intent and only its resolved semantic context, never the whole catalog.
/// </summary>
internal sealed record SqlReasoningRequest(
    CanonicalQuery Query,
    SemanticQueryContext InitialSemanticContext,
    SqlReasoningAllowedCapabilities AllowedCapabilities,
    SqlGenerationConstraints Constraints,
    ImmutableArray<SqlSemanticToolKind> AvailableSemanticTools);

internal sealed record SqlReasoningAllowedCapabilities(
    QueryComplexity Complexity,
    ImmutableArray<QueryFeature> QueryFeatures,
    ImmutableArray<QueryCapabilityReasonCode> CapabilityReasons,
    int MaximumExecutableJoins,
    bool JoinExecutionEnabled);

/// <summary>
/// Strict backend-owned generation constraints. These describe the candidate contract; they
/// do not assert that a returned SQL string obeys it. Common guardrails remain authoritative.
/// </summary>
internal sealed record SqlGenerationConstraints(
    bool SelectOnly,
    bool SingleStatementOnly,
    bool ExplicitProjectionRequired,
    bool ApprovedObjectsAndColumnsOnly,
    bool BackendOwnsLiteralParameterization,
    bool BackendOwnsDataScope,
    bool CandidateMustNotExecute,
    int MaximumCandidates);

internal enum SqlSemanticToolKind
{
    GetMetricContext,
    GetDimensionContext,
    GetSourceContext,
    GetQuerySemanticContext,
    GetRelationships,
    FindApprovedJoinPaths,
    ValidateApprovedJoinPath
}

internal enum SqlReasoningResponseStatus
{
    Candidate,
    UnableToResolve
}

/// <summary>
/// Structured model result. SQL is always an untrusted candidate. No parameter types,
/// authorization values, security parameters, raw model response or chain-of-thought can be
/// represented by this contract.
/// </summary>
internal sealed record SqlReasoningResponse
{
    public required SqlReasoningResponseStatus Status { get; init; }

    public string? Sql { get; init; }

    public ImmutableArray<string> ReferencedSemanticKeys { get; init; } = [];

    public ImmutableArray<string> ReferencedRelationshipIds { get; init; } = [];
}
