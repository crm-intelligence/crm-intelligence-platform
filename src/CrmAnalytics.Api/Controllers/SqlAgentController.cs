using CrmAnalytics.Api.Authentication;
using CrmAnalytics.Application.Identity;
using CrmAnalytics.Application.SqlAgent;
using CrmAnalytics.Contracts.SqlAgent;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrmAnalytics.Api.Controllers;

[ApiController]
[Route("api/sql-agent")]
[Authorize(Policy = AuthenticationPolicies.ReportsAccess)]
[Consumes("application/json")]
[Produces("application/json")]
[Tags("SQL Agent")]
[RequestSizeLimit(131_072)]
public sealed class SqlAgentController(
    ISqlAgentService sqlAgentService,
    ICurrentUserContextAccessor currentUserAccessor,
    IUserDataScopeResolver userDataScopeResolver) : ControllerBase
{
    [HttpPost("capabilities/analyze", Name = "analyze_query_capability")]
    [EndpointSummary("Classify a bound analytical intent using backend-owned capabilities")]
    [Authorize(Policy = AuthenticationPolicies.ReportsReadOwn)]
    [ProducesResponseType(typeof(SqlAgentCapabilityResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(SqlAgentErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(SqlAgentErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(SqlAgentErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(SqlAgentErrorResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<SqlAgentCapabilityResponse>> AnalyzeCapabilityAsync(
        [FromBody] SqlAgentIntentRequest request,
        CancellationToken cancellationToken) =>
        Result(await sqlAgentService.AnalyzeCapabilityAsync(
            request, currentUserAccessor.GetRequiredUser(), cancellationToken));

    [HttpPost("tools/query-context", Name = "get_query_semantic_context")]
    [EndpointSummary("Get minimum approved semantic context for a bound analytical intent")]
    [Authorize(Policy = AuthenticationPolicies.ReportsReadOwn)]
    [ProducesResponseType(typeof(SqlAgentQueryContextResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(SqlAgentErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(SqlAgentErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(SqlAgentErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(SqlAgentErrorResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<SqlAgentQueryContextResponse>> GetQueryContextAsync(
        [FromBody] SqlAgentIntentRequest request,
        CancellationToken cancellationToken) =>
        Result(await sqlAgentService.GetQueryContextAsync(
            request, currentUserAccessor.GetRequiredUser(), cancellationToken));

    [HttpPost("tools/metric-context", Name = "get_metric_context")]
    [EndpointSummary("Get approved context for an intent-scoped metric")]
    [Authorize(Policy = AuthenticationPolicies.ReportsReadOwn)]
    [ProducesResponseType(typeof(SqlAgentMetricContextResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(SqlAgentErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(SqlAgentErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(SqlAgentErrorResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<SqlAgentMetricContextResponse>> GetMetricContextAsync(
        [FromBody] SqlAgentMetricContextRequest request,
        CancellationToken cancellationToken) =>
        Result(await sqlAgentService.GetMetricContextAsync(
            request, currentUserAccessor.GetRequiredUser(), cancellationToken));

    [HttpPost("tools/dimension-context", Name = "get_dimension_context")]
    [EndpointSummary("Get approved context for an intent-scoped dimension")]
    [Authorize(Policy = AuthenticationPolicies.ReportsReadOwn)]
    [ProducesResponseType(typeof(SqlAgentDimensionContextResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(SqlAgentErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(SqlAgentErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(SqlAgentErrorResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<SqlAgentDimensionContextResponse>> GetDimensionContextAsync(
        [FromBody] SqlAgentDimensionContextRequest request,
        CancellationToken cancellationToken) =>
        Result(await sqlAgentService.GetDimensionContextAsync(
            request, currentUserAccessor.GetRequiredUser(), cancellationToken));

    [HttpPost("tools/source-context", Name = "get_source_context")]
    [EndpointSummary("Get minimum approved physical mapping for an intent-scoped source")]
    [Authorize(Policy = AuthenticationPolicies.ReportsReadOwn)]
    [ProducesResponseType(typeof(SqlAgentSourceContextResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(SqlAgentErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(SqlAgentErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(SqlAgentErrorResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<SqlAgentSourceContextResponse>> GetSourceContextAsync(
        [FromBody] SqlAgentSourceContextRequest request,
        CancellationToken cancellationToken) =>
        Result(await sqlAgentService.GetSourceContextAsync(
            request, currentUserAccessor.GetRequiredUser(), cancellationToken));

    [HttpPost("tools/relationships", Name = "get_relationships")]
    [EndpointSummary("Get reviewed relationships limited to the current intent context")]
    [Authorize(Policy = AuthenticationPolicies.ReportsReadOwn)]
    [ProducesResponseType(typeof(SqlAgentRelationshipsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(SqlAgentErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(SqlAgentErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(SqlAgentErrorResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<SqlAgentRelationshipsResponse>> GetRelationshipsAsync(
        [FromBody] SqlAgentRelationshipsRequest request,
        CancellationToken cancellationToken) =>
        Result(await sqlAgentService.GetRelationshipsAsync(
            request, currentUserAccessor.GetRequiredUser(), cancellationToken));

    [HttpPost("tools/join-paths/find", Name = "find_approved_join_paths")]
    [EndpointSummary("Find approved join paths within the current intent context")]
    [Authorize(Policy = AuthenticationPolicies.ReportsReadOwn)]
    [ProducesResponseType(typeof(SqlAgentJoinPathsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(SqlAgentErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(SqlAgentErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(SqlAgentErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(SqlAgentErrorResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<SqlAgentJoinPathsResponse>> FindJoinPathsAsync(
        [FromBody] SqlAgentFindJoinPathsRequest request,
        CancellationToken cancellationToken) =>
        Result(await sqlAgentService.FindJoinPathsAsync(
            request, currentUserAccessor.GetRequiredUser(), cancellationToken));

    [HttpPost("tools/join-paths/validate", Name = "validate_approved_join_path")]
    [EndpointSummary("Validate a proposed path against reviewed relationship contracts")]
    [Authorize(Policy = AuthenticationPolicies.ReportsReadOwn)]
    [ProducesResponseType(typeof(SqlAgentJoinPathValidationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(SqlAgentErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(SqlAgentErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(SqlAgentErrorResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<SqlAgentJoinPathValidationResponse>> ValidateJoinPathAsync(
        [FromBody] SqlAgentValidateJoinPathRequest request,
        CancellationToken cancellationToken) =>
        Result(await sqlAgentService.ValidateJoinPathAsync(
            request, currentUserAccessor.GetRequiredUser(), cancellationToken));

    [HttpPost("candidates", Name = "submit_sql_candidate")]
    [EndpointSummary("Validate one untrusted SQL candidate through the common guardrails")]
    [Authorize(Policy = AuthenticationPolicies.ReportsCreate)]
    [ProducesResponseType(typeof(SqlAgentCandidateSubmissionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(SqlAgentErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(SqlAgentErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(SqlAgentErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(SqlAgentErrorResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<SqlAgentCandidateSubmissionResponse>> SubmitCandidateAsync(
        [FromBody] SqlAgentCandidateSubmissionRequest request,
        CancellationToken cancellationToken)
    {
        var user = currentUserAccessor.GetRequiredUser();
        var scope = await userDataScopeResolver.ResolveRequiredAsync(
            user, cancellationToken);
        return Result(await sqlAgentService.SubmitCandidateAsync(
            request, user, scope, cancellationToken));
    }

    private ActionResult<T> Result<T>(SqlAgentServiceResult<T> result) where T : class =>
        result.IsSuccessful
            ? Ok(result.Value)
            : StatusCode(result.StatusCode, result.Error);
}
