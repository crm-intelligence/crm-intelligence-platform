namespace CrmAnalytics.Domain.ReportRequests;

public enum ReportRequestStatus {
    Received = 1,
    Validating = 2,
    Queued = 3,
    Processing = 4,
    Running = 5,
    WaitingForClarification = 6,
    Completed = 7,
    Failed = 8,
    Rejected = 9
}
