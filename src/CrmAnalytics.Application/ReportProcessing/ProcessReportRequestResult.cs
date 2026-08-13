using CrmAnalytics.Domain.ReportRequests;

namespace CrmAnalytics.Application.ReportProcessing;

public sealed record ProcessReportRequestResult(
    string RequestId,
    ReportRequestStatus Status,
    DateTimeOffset UpdatedAt,
    string? Summary,
    string? PowerBiUrl,
    string? ClarificationQuestion,
    string? ErrorCode,
    string? RejectionCode = null,
    string? RejectionMessage = null);
