namespace CrmAnalytics.Application.ReportRequests;

public sealed record RecordCanonicalRequestCommand(
    string RequestId,
    string CanonicalRequestJson,
    DateTimeOffset RecordedAt);
