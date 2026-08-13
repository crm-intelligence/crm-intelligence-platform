namespace CrmAnalytics.Teams.Planning;

public class CopilotStudioPlanningException : Exception
{
    public CopilotStudioPlanningException(string message)
        : base(message)
    {
    }

    public CopilotStudioPlanningException(
        string message,
        Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class CopilotStudioPlanningTimeoutException
    : CopilotStudioPlanningException
{
    public CopilotStudioPlanningTimeoutException(
        string message,
        Exception innerException)
        : base(message, innerException)
    {
    }
}
