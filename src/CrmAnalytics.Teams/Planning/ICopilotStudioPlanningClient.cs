using CrmAnalytics.Contracts.CopilotStudio;

namespace CrmAnalytics.Teams.Planning;

public interface ICopilotStudioPlanningClient
{
    Task<CopilotPlannedReportRequest> PlanAsync(
        string prompt,
        string conversationId,
        CancellationToken cancellationToken);

    Task<CopilotPlannedReportRequest> PlanAsync(
        CopilotPhase2PlanningRequest request,
        string conversationId,
        CancellationToken cancellationToken) =>
        PlanAsync(
            CopilotPhase2PromptFormatter.Format(request),
            conversationId,
            cancellationToken);
}
