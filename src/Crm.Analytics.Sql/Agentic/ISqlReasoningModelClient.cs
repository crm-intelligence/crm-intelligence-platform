namespace Crm.Analytics.Sql.Agentic;

/// <summary>
/// Provider-agnostic single-candidate reasoning boundary. The client receives no connection,
/// credential, authorization, data-scope or execution dependency.
/// </summary>
internal interface ISqlReasoningModelClient
{
    Task<SqlReasoningResponse> GenerateAsync(
        SqlReasoningRequest request,
        CancellationToken cancellationToken);
}
