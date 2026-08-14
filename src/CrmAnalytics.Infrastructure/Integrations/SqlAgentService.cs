using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Crm.Analytics.Sql.Agentic;
using Crm.Analytics.Sql.Catalog;
using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.Contracts.V2;
using Crm.Analytics.Sql.Routing;
using Crm.Analytics.Sql.SemanticContext;
using Crm.Analytics.Sql.Service;
using CrmAnalytics.Application.Abstractions.Persistence;
using CrmAnalytics.Application.Identity;
using CrmAnalytics.Application.ReportRequests;
using CrmAnalytics.Application.SqlAgent;
using CrmAnalytics.Application.SqlProduction;
using CrmAnalytics.Contracts.Integrations;
using CrmAnalytics.Contracts.SqlAgent;
using CrmAnalytics.Domain.ReportRequests;
using Microsoft.Extensions.Logging;

namespace CrmAnalytics.Infrastructure.Integrations;

internal sealed class SqlAgentService(
    IReportRequestRepository repository,
    IReportRequestAccessService accessService,
    ISqlAgentBackend backend,
    SqlProductionScopeCompatibilityMapper scopeMapper,
    ILogger<SqlAgentService> logger) : ISqlAgentService
{
    private const string ToolSucceeded = "succeeded";
    private const string ToolUnsupported = "unsupported";
    private const string ToolNotFound = "not_found";
    private const string ToolRejected = "rejected";

    public async Task<SqlAgentServiceResult<SqlAgentCapabilityResponse>>
        AnalyzeCapabilityAsync(
            SqlAgentIntentRequest request,
            AuthenticatedUserContext user,
            CancellationToken cancellationToken)
    {
        var bound = await BindAsync(
            request, user, requireIntentBinding: false, cancellationToken);
        if (bound.Error is not null)
        {
            return Failure<SqlAgentCapabilityResponse>(bound.Error);
        }

        var decision = backend.Analyze(DataSource.Dwh, bound.Query!);
        if (decision.Outcome == QueryCapabilityOutcome.Unsupported)
        {
            Log("analyze_query_capability", ToolUnsupported, FirstReason(decision));
            return Success(new SqlAgentCapabilityResponse(
                ToolUnsupported,
                Name(decision.Outcome),
                Name(decision.Complexity),
                decision.Features.Select(Name).ToArray(),
                decision.Reasons.Select(Name).ToArray(),
                null));
        }

        var bindingError = ValidateIntentBinding(bound.Canonical!, bound.Query!);
        if (bindingError is not null)
        {
            Log("analyze_query_capability", ToolRejected, bindingError.ReasonCode);
            return Failure<SqlAgentCapabilityResponse>(bindingError);
        }

        var fingerprint = CreateFingerprint(
            request.RequestId, bound.CanonicalJson!, request.Intent);
        Log("analyze_query_capability", ToolSucceeded, Name(decision.Outcome));
        return Success(new SqlAgentCapabilityResponse(
            ToolSucceeded,
            Name(decision.Outcome),
            Name(decision.Complexity),
            decision.Features.Select(Name).ToArray(),
            decision.Reasons.Select(Name).ToArray(),
            fingerprint));
    }

    public async Task<SqlAgentServiceResult<SqlAgentQueryContextResponse>>
        GetQueryContextAsync(
            SqlAgentIntentRequest request,
            AuthenticatedUserContext user,
            CancellationToken cancellationToken)
    {
        var bound = await BindAsync(request, user, true, cancellationToken);
        if (bound.Error is not null)
        {
            return Failure<SqlAgentQueryContextResponse>(bound.Error);
        }

        var context = GetValidatedContext(bound.Query!);
        if (context.Error is not null)
        {
            return Failure<SqlAgentQueryContextResponse>(context.Error, context.StatusCode);
        }

        var value = context.Value!;
        Log("get_query_semantic_context", ToolSucceeded, null);
        return Success(new SqlAgentQueryContextResponse(
            ToolSucceeded,
            CreateFingerprint(request.RequestId, bound.CanonicalJson!, request.Intent),
            value.Metrics.Select(MapMetric).ToArray(),
            value.Dimensions.Select(MapDimension).ToArray(),
            value.TimeDimensions.Select(MapDimension).ToArray(),
            value.Sources.Select(MapSource).ToArray(),
            value.Relationships.Select(MapRelationship).ToArray(),
            new SqlAgentContextCapabilitiesResponse(
                value.Capabilities.MaximumExecutableJoins,
                value.Capabilities.ApprovedRelationshipCount,
                value.Capabilities.JoinExecutionEnabled,
                value.Capabilities.ApprovedPathDiscoveryAvailable)));
    }

    public async Task<SqlAgentServiceResult<SqlAgentMetricContextResponse>>
        GetMetricContextAsync(
            SqlAgentMetricContextRequest request,
            AuthenticatedUserContext user,
            CancellationToken cancellationToken)
    {
        var context = await GetBoundContextAsync(request, user, cancellationToken);
        if (context.Error is not null)
        {
            return Failure<SqlAgentMetricContextResponse>(context.Error, context.StatusCode);
        }

        if (!context.Value!.Metrics.Any(metric =>
                string.Equals(metric.Key, request.MetricKey, StringComparison.Ordinal)))
        {
            return RejectUnrelated<SqlAgentMetricContextResponse>("get_metric_context");
        }

        var result = backend.GetMetricContext(DataSource.Dwh, request.MetricKey);
        if (!result.IsSuccessful)
        {
            return SemanticFailure<SqlAgentMetricContextResponse>(
                "get_metric_context", result.Failure!);
        }

        Log("get_metric_context", ToolSucceeded, null);
        return Success(MapMetric(result.Value!));
    }

    public async Task<SqlAgentServiceResult<SqlAgentDimensionContextResponse>>
        GetDimensionContextAsync(
            SqlAgentDimensionContextRequest request,
            AuthenticatedUserContext user,
            CancellationToken cancellationToken)
    {
        var context = await GetBoundContextAsync(request, user, cancellationToken);
        if (context.Error is not null)
        {
            return Failure<SqlAgentDimensionContextResponse>(context.Error, context.StatusCode);
        }

        var allowed = context.Value!.Dimensions.Concat(context.Value.TimeDimensions)
            .Any(dimension => string.Equals(
                dimension.Key, request.DimensionKey, StringComparison.Ordinal));
        if (!allowed)
        {
            return RejectUnrelated<SqlAgentDimensionContextResponse>(
                "get_dimension_context");
        }

        var result = backend.GetDimensionContext(DataSource.Dwh, request.DimensionKey);
        if (!result.IsSuccessful)
        {
            return SemanticFailure<SqlAgentDimensionContextResponse>(
                "get_dimension_context", result.Failure!);
        }

        Log("get_dimension_context", ToolSucceeded, null);
        return Success(MapDimension(result.Value!));
    }

    public async Task<SqlAgentServiceResult<SqlAgentSourceContextResponse>>
        GetSourceContextAsync(
            SqlAgentSourceContextRequest request,
            AuthenticatedUserContext user,
            CancellationToken cancellationToken)
    {
        var context = await GetBoundContextAsync(request, user, cancellationToken);
        if (context.Error is not null)
        {
            return Failure<SqlAgentSourceContextResponse>(context.Error, context.StatusCode);
        }

        var scoped = context.Value!.Sources.FirstOrDefault(source => string.Equals(
            source.LogicalSource, request.LogicalSource,
            StringComparison.OrdinalIgnoreCase));
        if (scoped is null)
        {
            return RejectUnrelated<SqlAgentSourceContextResponse>("get_source_context");
        }

        var result = backend.GetSourceContext(DataSource.Dwh, request.LogicalSource);
        if (!result.IsSuccessful)
        {
            return SemanticFailure<SqlAgentSourceContextResponse>(
                "get_source_context", result.Failure!);
        }

        var approved = result.Value!;
        var minimum = approved with { ApprovedColumns = scoped.ApprovedColumns };
        Log("get_source_context", ToolSucceeded, null);
        return Success(MapSource(minimum));
    }

    public async Task<SqlAgentServiceResult<SqlAgentRelationshipsResponse>>
        GetRelationshipsAsync(
            SqlAgentRelationshipsRequest request,
            AuthenticatedUserContext user,
            CancellationToken cancellationToken)
    {
        var context = await GetBoundContextAsync(request, user, cancellationToken);
        if (context.Error is not null)
        {
            return Failure<SqlAgentRelationshipsResponse>(context.Error, context.StatusCode);
        }

        var sources = context.Value!.Sources.Select(source => source.LogicalSource)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (request.LogicalSources.Any(source => !sources.Contains(source)))
        {
            return RejectUnrelated<SqlAgentRelationshipsResponse>("get_relationships");
        }

        var result = backend.GetRelationships(DataSource.Dwh, request.LogicalSources);
        if (!result.IsSuccessful)
        {
            return SemanticFailure<SqlAgentRelationshipsResponse>(
                "get_relationships", result.Failure!);
        }

        var scopedIds = context.Value.Relationships.Select(item => item.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var relationships = result.Value!.Items
            .Where(item => scopedIds.Contains(item.Id))
            .Select(MapRelationship)
            .ToArray();
        Log("get_relationships", ToolSucceeded, null);
        return Success(new SqlAgentRelationshipsResponse(ToolSucceeded, relationships));
    }

    public async Task<SqlAgentServiceResult<SqlAgentJoinPathsResponse>>
        FindJoinPathsAsync(
            SqlAgentFindJoinPathsRequest request,
            AuthenticatedUserContext user,
            CancellationToken cancellationToken)
    {
        var context = await GetBoundContextAsync(request, user, cancellationToken);
        if (context.Error is not null)
        {
            return Failure<SqlAgentJoinPathsResponse>(context.Error, context.StatusCode);
        }

        var sources = context.Value!.Sources.Select(source => source.LogicalSource)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!sources.Contains(request.FromSource) || !sources.Contains(request.ToSource))
        {
            return RejectUnrelated<SqlAgentJoinPathsResponse>("find_approved_join_paths");
        }

        var result = backend.FindApprovedJoinPaths(
            DataSource.Dwh, request.FromSource, request.ToSource, request.MaxHops);
        if (result.Status == JoinPathResolutionStatus.InvalidRequest)
        {
            return ControlledFailure<SqlAgentJoinPathsResponse>(
                "find_approved_join_paths", "invalid", "INVALID_RELATIONSHIP_REQUEST", 400);
        }

        var scopedIds = context.Value.Relationships.Select(item => item.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var paths = result.Paths
            .Where(path => path.Steps.All(step =>
                scopedIds.Contains(step.Relationship.Id)))
            .Select(path => new SqlAgentJoinPathResponse(path.Steps.Select(step =>
                new SqlAgentJoinPathStepResponse(
                    step.Relationship.Id,
                    step.FromSource,
                    step.ToSource,
                    Name(step.Relationship.AllowedJoinTypes[0]))).ToArray()))
            .ToArray();
        var status = result.Status switch
        {
            JoinPathResolutionStatus.Found => "found",
            JoinPathResolutionStatus.Ambiguous => "ambiguous",
            _ => ToolNotFound
        };
        Log("find_approved_join_paths", status, null);
        return Success(new SqlAgentJoinPathsResponse(status, paths));
    }

    public async Task<SqlAgentServiceResult<SqlAgentJoinPathValidationResponse>>
        ValidateJoinPathAsync(
            SqlAgentValidateJoinPathRequest request,
            AuthenticatedUserContext user,
            CancellationToken cancellationToken)
    {
        var context = await GetBoundContextAsync(request, user, cancellationToken);
        if (context.Error is not null)
        {
            return Failure<SqlAgentJoinPathValidationResponse>(context.Error, context.StatusCode);
        }

        var sources = context.Value!.Sources.Select(source => source.LogicalSource)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var relationships = context.Value.Relationships.Select(item => item.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!sources.Contains(request.FromSource)
            || !sources.Contains(request.ToSource)
            || request.Steps.Any(step =>
                !sources.Contains(step.FromSource)
                || !sources.Contains(step.ToSource)
                || !relationships.Contains(step.RelationshipId)))
        {
            return RejectUnrelated<SqlAgentJoinPathValidationResponse>(
                "validate_approved_join_path");
        }

        var proposal = new JoinPathProposal(
            request.FromSource,
            request.ToSource,
            request.Steps.Select(step => new JoinPathProposalStep(
                step.RelationshipId,
                step.FromSource,
                step.ToSource,
                step.JoinType == SqlAgentJoinType.Inner
                    ? ApprovedJoinType.Inner
                    : ApprovedJoinType.Left)).ToImmutableArray());
        var result = backend.ValidateApprovedJoinPath(DataSource.Dwh, proposal);
        var status = result.IsValid ? "valid" : ToolUnsupported;
        Log("validate_approved_join_path", status, Name(result.Code));
        return Success(new SqlAgentJoinPathValidationResponse(
            status,
            result.IsValid ? null : Name(result.Code)));
    }

    public async Task<SqlAgentServiceResult<SqlAgentCandidateSubmissionResponse>>
        SubmitCandidateAsync(
            SqlAgentCandidateSubmissionRequest request,
            AuthenticatedUserContext user,
            CrmAnalytics.Contracts.Integrations.UserDataScope userDataScope,
            CancellationToken cancellationToken)
    {
        var bound = await BindAsync(request, user, true, cancellationToken);
        if (bound.Error is not null)
        {
            return Failure<SqlAgentCandidateSubmissionResponse>(bound.Error);
        }

        if (bound.Report!.Status is not (ReportRequestStatus.Processing
            or ReportRequestStatus.Running))
        {
            return ControlledFailure<SqlAgentCandidateSubmissionResponse>(
                "submit_sql_candidate", "conflict", "REQUEST_NOT_ACCEPTING_CANDIDATES", 409);
        }

        var expectedFingerprint = CreateFingerprint(
            request.RequestId, bound.CanonicalJson!, request.Intent);
        if (!FixedTimeEquals(expectedFingerprint, request.ContextFingerprint))
        {
            return ControlledFailure<SqlAgentCandidateSubmissionResponse>(
                "submit_sql_candidate", ToolRejected, "CANDIDATE_REQUEST_BINDING_MISMATCH", 409);
        }

        var decision = backend.Analyze(DataSource.Dwh, bound.Query!);
        if (decision.Outcome != QueryCapabilityOutcome.AgenticRequired)
        {
            return ControlledFailure<SqlAgentCandidateSubmissionResponse>(
                "submit_sql_candidate", ToolUnsupported,
                decision.Outcome == QueryCapabilityOutcome.Deterministic
                    ? "DETERMINISTIC_STRATEGY_REQUIRED"
                    : FirstReason(decision),
                422);
        }

        var mappedScope = scopeMapper.Map(userDataScope);
        var productionRequest = new SqlProductionRequest
        {
            Prompt = "sql-agent-candidate",
            RequestId = bound.Report.RequestId,
            ConversationId = bound.Report.ConversationId,
            Scope = mappedScope.Kind switch
            {
                SqlProductionScopeMappingKind.Unrestricted =>
                    Crm.Analytics.Sql.Contracts.UserDataScope.Unrestricted,
                SqlProductionScopeMappingKind.Regions =>
                    Crm.Analytics.Sql.Contracts.UserDataScope.ForRegions(
                        mappedScope.Regions.ToArray()),
                _ => Crm.Analytics.Sql.Contracts.UserDataScope.Unresolved
            },
            Today = bound.Report.ReferenceDate,
            UserId = user.UserId,
            Source = DataSource.Dwh
        };
        var candidate = new SqlReasoningResponse
        {
            Status = SqlReasoningResponseStatus.Candidate,
            Sql = request.CandidateSql,
            ReferencedSemanticKeys = request.ReferencedSemanticKeys.ToImmutableArray(),
            ReferencedRelationshipIds = request.ReferencedRelationshipIds.ToImmutableArray()
        };
        var produced = backend.ValidateCandidate(
            productionRequest, bound.Canonical!, bound.Query!, candidate);
        var mapped = CrmAnalyticsSqlProductionClient.MapResponse(produced);
        if (mapped.Decision != SqlProductionClientDecision.Accepted
            || mapped.ExecutionPlan is null)
        {
            Log("submit_sql_candidate", ToolRejected, produced.ReasonCode.ToString());
            return new SqlAgentServiceResult<SqlAgentCandidateSubmissionResponse>(
                null,
                new SqlAgentErrorResponse(ToolRejected, produced.ReasonCode.ToString()),
                422);
        }

        var plan = mapped.ExecutionPlan;
        Log("submit_sql_candidate", ToolSucceeded, "agentic");
        return new SqlAgentServiceResult<SqlAgentCandidateSubmissionResponse>(
            new SqlAgentCandidateSubmissionResponse(
                ToolSucceeded,
                "agentic",
                Name(plan.Source),
                plan.VerifiedPhysicalObject!,
                plan.RowLimit!.Value,
                plan.CommandTimeoutSeconds),
            null,
            200,
            plan);
    }

    private async Task<SqlAgentServiceResult<SemanticQueryContext>>
        GetBoundContextAsync(
            SqlAgentIntentRequest request,
            AuthenticatedUserContext user,
            CancellationToken cancellationToken)
    {
        var bound = await BindAsync(request, user, true, cancellationToken);
        return bound.Error is not null
            ? Failure<SemanticQueryContext>(bound.Error)
            : GetValidatedContext(bound.Query!);
    }

    private SqlAgentServiceResult<SemanticQueryContext> GetValidatedContext(
        CanonicalQuery query)
    {
        var decision = backend.Analyze(DataSource.Dwh, query);
        if (decision.Outcome == QueryCapabilityOutcome.Unsupported)
        {
            return new SqlAgentServiceResult<SemanticQueryContext>(
                null,
                new SqlAgentErrorResponse(ToolUnsupported, FirstReason(decision)),
                422);
        }

        var context = backend.GetQuerySemanticContext(DataSource.Dwh, query);
        return context.IsSuccessful
            ? Success(context.Value!)
            : new SqlAgentServiceResult<SemanticQueryContext>(
                null,
                new SqlAgentErrorResponse(
                    ToolUnsupported, Name(context.Failure!.Code)),
                422);
    }

    private async Task<BoundRequest> BindAsync(
        SqlAgentIntentRequest request,
        AuthenticatedUserContext user,
        bool requireIntentBinding,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(user);

        var report = await repository.GetByIdAsync(
            request.RequestId, cancellationToken);
        if (report is null)
        {
            return BoundRequest.Fail(ToolNotFound, "REQUEST_NOT_FOUND", 404);
        }

        try
        {
            accessService.EnsureCanAccess(report, user);
        }
        catch (KeyNotFoundException)
        {
            return BoundRequest.Fail(ToolNotFound, "REQUEST_NOT_FOUND", 404);
        }

        if (report.Status is ReportRequestStatus.Completed
            or ReportRequestStatus.Failed
            or ReportRequestStatus.Rejected)
        {
            return BoundRequest.Fail("conflict", "REQUEST_TERMINAL", 409);
        }

        if (string.IsNullOrWhiteSpace(report.CanonicalRequestJson))
        {
            return BoundRequest.Fail("conflict", "CANONICAL_INTENT_NOT_READY", 409);
        }

        CanonicalRequest canonical;
        CanonicalQuery query;
        try
        {
            canonical = CanonicalRequestSerializer.Deserialize(report.CanonicalRequestJson);
            query = MapIntent(request.Intent);
        }
        catch (Exception exception) when (exception is ArgumentException
            or JsonException or NotSupportedException)
        {
            return BoundRequest.Fail("invalid", "INVALID_CANONICAL_INTENT", 400);
        }

        if (!string.Equals(canonical.RequestId, report.RequestId, StringComparison.Ordinal)
            || !string.Equals(
                canonical.ConversationId, report.ConversationId,
                StringComparison.Ordinal)
            || canonical.Source != DataSource.Dwh)
        {
            return BoundRequest.Fail(ToolRejected, "REQUEST_CANONICAL_BINDING_INVALID", 409);
        }

        if (requireIntentBinding)
        {
            var error = ValidateIntentBinding(canonical, query);
            if (error is not null)
            {
                return new BoundRequest(null, null, null, null, error);
            }
        }

        return new BoundRequest(
            report, canonical, query, report.CanonicalRequestJson, null);
    }

    private static SqlAgentError? ValidateIntentBinding(
        CanonicalRequest canonical,
        CanonicalQuery query)
    {
        var baseline = CanonicalV1ToV2Adapter.Adapt(canonical);
        var sameBase = query.Version == CanonicalQuery.CurrentVersion
            && baseline.Metrics.SequenceEqual(query.Metrics, StringComparer.Ordinal)
            && baseline.Dimensions.SequenceEqual(query.Dimensions, StringComparer.Ordinal)
            && FiltersEqual(baseline.Filters, query.Filters)
            && baseline.Time == query.Time;
        if (!sameBase)
        {
            return new SqlAgentError(ToolRejected, "INTENT_REQUEST_MISMATCH", 409);
        }

        var baselineDimensionOrdering = baseline.Ordering
            .Where(item => item.TargetKind == CanonicalOrderingTargetKind.Dimension)
            .ToArray();
        var queryDimensionOrdering = query.Ordering
            .Where(item => item.TargetKind == CanonicalOrderingTargetKind.Dimension)
            .ToArray();
        if (!baselineDimensionOrdering.SequenceEqual(queryDimensionOrdering)
            || baseline.Limit is not null && baseline.Limit != query.Limit)
        {
            return new SqlAgentError(ToolRejected, "INTENT_REQUEST_MISMATCH", 409);
        }

        return null;
    }

    private static bool FiltersEqual(
        IReadOnlyList<RequestFilter> left,
        IReadOnlyList<RequestFilter> right) =>
        left.Count == right.Count && left.Zip(right).All(pair =>
            pair.First.Field == pair.Second.Field
            && pair.First.Op == pair.Second.Op
            && pair.First.Values.SequenceEqual(pair.Second.Values));

    private static CanonicalQuery MapIntent(CopilotSqlAgentIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        return new CanonicalQuery
        {
            Version = intent.Version,
            Metrics = intent.Metrics.ToArray(),
            Dimensions = intent.Dimensions.ToArray(),
            Filters = intent.Filters.Select(filter => new RequestFilter
            {
                Field = filter.Field,
                Op = (FilterOperator)filter.Operator,
                Values = filter.Values.Select(value => new FilterLiteral(
                    (FilterValueKind)value.Kind, value.Value)).ToArray()
            }).ToArray(),
            Time = new CanonicalTimeIntent
            {
                Range = new DateRangeSpec
                {
                    Kind = (DateRangeKind)intent.Time.Range.Kind,
                    RelativeExpression = intent.Time.Range.RelativeExpression,
                    From = intent.Time.Range.From,
                    To = intent.Time.Range.To
                },
                Grain = (TimeGrain)intent.Time.Grain
            },
            Comparisons = intent.Comparisons.Select(item =>
                new CanonicalPeriodComparison
                {
                    Kind = (PeriodComparisonKind)item.Kind
                }).ToArray(),
            Calculations = intent.Calculations.Select(item =>
                new CanonicalCalculation
                {
                    Kind = (CanonicalCalculationKind)item.Kind,
                    MetricKeys = item.MetricKeys.ToArray()
                }).ToArray(),
            Ordering = intent.Ordering.Select(item => new CanonicalOrdering
            {
                TargetKind = (CanonicalOrderingTargetKind)item.TargetKind,
                TargetKey = item.TargetKey,
                Direction = (SortDirection)item.Direction
            }).ToArray(),
            Limit = intent.Limit is null
                ? null
                : new CanonicalLimit
                {
                    Count = intent.Limit.Count,
                    Kind = (CanonicalLimitKind)intent.Limit.Kind
                }
        };
    }

    private static SqlAgentMetricContextResponse MapMetric(
        SemanticMetricContext metric) => new(
            metric.Key,
            metric.Label,
            metric.Description,
            metric.Kind,
            metric.ValueType,
            metric.Unit,
            metric.ApprovedExpression,
            metric.ApprovedExpressionColumns,
            metric.LogicalSource,
            metric.CompatibleDimensions,
            metric.CompatibleFilters,
            metric.RequiresDateRange);

    private static SqlAgentDimensionContextResponse MapDimension(
        SemanticDimensionContext dimension) => new(
            dimension.Key,
            dimension.Label,
            dimension.Description,
            dimension.LogicalSource,
            dimension.ApprovedPhysicalColumn,
            dimension.ValueType,
            dimension.Selectable,
            dimension.Groupable,
            dimension.Filterable,
            dimension.Sortable,
            dimension.IsTimeDimension,
            dimension.SemanticRole);

    private static SqlAgentSourceContextResponse MapSource(
        SemanticSourceContext source) => new(
            source.LogicalSource,
            source.ApprovedPhysicalObject,
            Name(source.Runtime),
            source.ApprovedColumns);

    private static SqlAgentRelationshipResponse MapRelationship(
        ApprovedRelationshipDefinition relationship) => new(
            relationship.Id,
            relationship.LeftSource,
            relationship.LeftPhysicalObject,
            relationship.LeftColumn,
            relationship.RightSource,
            relationship.RightPhysicalObject,
            relationship.RightColumn,
            Name(relationship.Cardinality),
            relationship.AllowedJoinTypes.Select(Name).ToArray());

    private static string CreateFingerprint(
        string requestId,
        string canonicalJson,
        CopilotSqlAgentIntent intent)
    {
        var intentJson = JsonSerializer.Serialize(intent);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(
            requestId + "\n" + canonicalJson + "\n" + intentJson));
        return Convert.ToHexStringLower(bytes);
    }

    private static bool FixedTimeEquals(string expected, string actual)
    {
        if (expected.Length != actual.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expected),
            Encoding.ASCII.GetBytes(actual.ToLowerInvariant()));
    }

    private static string FirstReason(QueryStrategyDecision decision) =>
        decision.Reasons.Count == 0 ? "UNSUPPORTED" : Name(decision.Reasons[0]);

    private static string Name<T>(T value) where T : Enum
    {
        var name = value.ToString();
        var result = new StringBuilder(name.Length + 8);
        for (var index = 0; index < name.Length; index++)
        {
            var character = name[index];
            if (char.IsUpper(character) && index > 0)
            {
                result.Append('_');
            }
            result.Append(char.ToLowerInvariant(character));
        }
        return result.ToString();
    }

    private SqlAgentServiceResult<T> RejectUnrelated<T>(string tool) where T : class =>
        ControlledFailure<T>(tool, ToolUnsupported, "UNRELATED_SEMANTIC_CONTEXT", 422);

    private SqlAgentServiceResult<T> SemanticFailure<T>(
        string tool,
        SemanticContextFailure failure) where T : class =>
        ControlledFailure<T>(tool, ToolUnsupported, Name(failure.Code), 422);

    private SqlAgentServiceResult<T> ControlledFailure<T>(
        string tool,
        string status,
        string reason,
        int statusCode) where T : class
    {
        Log(tool, status, reason);
        return new SqlAgentServiceResult<T>(
            null, new SqlAgentErrorResponse(status, reason), statusCode);
    }

    private static SqlAgentServiceResult<T> Success<T>(T value) where T : class =>
        new(value, null, 200);

    private static SqlAgentServiceResult<T> Failure<T>(SqlAgentError error)
        where T : class =>
        new(null, new SqlAgentErrorResponse(error.Status, error.ReasonCode), error.StatusCode);

    private static SqlAgentServiceResult<T> Failure<T>(
        SqlAgentErrorResponse error,
        int statusCode) where T : class =>
        new(null, error, statusCode);

    private void Log(string tool, string outcome, string? failureCategory) =>
        logger.LogInformation(
            "SQL agent tool {ToolName} completed with {Outcome}; failure category {FailureCategory}.",
            tool,
            outcome,
            failureCategory);

    private sealed record BoundRequest(
        ReportRequest? Report,
        CanonicalRequest? Canonical,
        CanonicalQuery? Query,
        string? CanonicalJson,
        SqlAgentError? Error)
    {
        public static BoundRequest Fail(string status, string reasonCode, int statusCode) =>
            new(null, null, null, null, new SqlAgentError(status, reasonCode, statusCode));
    }

    private sealed record SqlAgentError(string Status, string ReasonCode, int StatusCode);
}
