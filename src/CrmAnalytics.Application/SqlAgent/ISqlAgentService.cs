using CrmAnalytics.Application.Identity;
using CrmAnalytics.Application.SqlProduction;
using CrmAnalytics.Contracts.Integrations;
using CrmAnalytics.Contracts.SqlAgent;

namespace CrmAnalytics.Application.SqlAgent;

public interface ISqlAgentService
{
    Task<SqlAgentServiceResult<SqlAgentCapabilityResponse>> AnalyzeCapabilityAsync(
        SqlAgentIntentRequest request,
        AuthenticatedUserContext user,
        CancellationToken cancellationToken);

    Task<SqlAgentServiceResult<SqlAgentQueryContextResponse>> GetQueryContextAsync(
        SqlAgentIntentRequest request,
        AuthenticatedUserContext user,
        CancellationToken cancellationToken);

    Task<SqlAgentServiceResult<SqlAgentMetricContextResponse>> GetMetricContextAsync(
        SqlAgentMetricContextRequest request,
        AuthenticatedUserContext user,
        CancellationToken cancellationToken);

    Task<SqlAgentServiceResult<SqlAgentDimensionContextResponse>> GetDimensionContextAsync(
        SqlAgentDimensionContextRequest request,
        AuthenticatedUserContext user,
        CancellationToken cancellationToken);

    Task<SqlAgentServiceResult<SqlAgentSourceContextResponse>> GetSourceContextAsync(
        SqlAgentSourceContextRequest request,
        AuthenticatedUserContext user,
        CancellationToken cancellationToken);

    Task<SqlAgentServiceResult<SqlAgentRelationshipsResponse>> GetRelationshipsAsync(
        SqlAgentRelationshipsRequest request,
        AuthenticatedUserContext user,
        CancellationToken cancellationToken);

    Task<SqlAgentServiceResult<SqlAgentJoinPathsResponse>> FindJoinPathsAsync(
        SqlAgentFindJoinPathsRequest request,
        AuthenticatedUserContext user,
        CancellationToken cancellationToken);

    Task<SqlAgentServiceResult<SqlAgentJoinPathValidationResponse>> ValidateJoinPathAsync(
        SqlAgentValidateJoinPathRequest request,
        AuthenticatedUserContext user,
        CancellationToken cancellationToken);

    Task<SqlAgentServiceResult<SqlAgentCandidateSubmissionResponse>> SubmitCandidateAsync(
        SqlAgentCandidateSubmissionRequest request,
        AuthenticatedUserContext user,
        UserDataScope userDataScope,
        CancellationToken cancellationToken);
}

public sealed record SqlAgentServiceResult<T>(
    T? Value,
    SqlAgentErrorResponse? Error,
    int StatusCode,
    SqlExecutionPlan? ExecutionPlan = null)
    where T : class
{
    public bool IsSuccessful => Value is not null && Error is null;
}
