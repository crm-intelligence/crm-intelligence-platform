namespace CrmAnalytics.Application.Auditing;

public enum ApplicationAuditEventType
{
    ReportCreated,
    ReportRevisionCreated,
    ReportClarificationSubmitted,
    CanonicalRequestRecorded,
    ReportClarificationRequested,
    ReportCompleted,
    ReportRejected,
    ReportFailed,
    QueryExecutionStarted,
    QueryExecutionSucceeded,
    QueryExecutionFailed
}
