namespace CrmAnalytics.Contracts.Integrations;

public enum ExternalOperationStatus
{
    Accepted = 1,
    Running = 2,
    WaitingForClarification = 3,
    Completed = 4,
    Failed = 5
}
