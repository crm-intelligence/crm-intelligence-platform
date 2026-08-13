namespace CrmAnalytics.Contracts.Common;

public sealed record ApiErrorResponse(
    string? RequestId,
    string ErrorCode,
    string Message,
    string TraceId);
