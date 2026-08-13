using System.Text.Json;
using CrmAnalytics.Contracts.Integrations;

namespace CrmAnalytics.Teams.Planning;

public sealed record CopilotPhase2PlanningRequest(
    string Mode,
    string OriginalRequest,
    SubmittedSemanticPlanningResult? CurrentSemanticPlan,
    string? ClarificationQuestion,
    string UserInput);

internal static class CopilotPhase2PromptFormatter
{
    private static readonly JsonSerializerOptions JsonOptions = new(
        JsonSerializerDefaults.Web)
    {
        MaxDepth = 32
    };

    public static string Format(CopilotPhase2PlanningRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var currentPlan = request.CurrentSemanticPlan is null
            ? "null"
            : JsonSerializer.Serialize(
                request.CurrentSemanticPlan, JsonOptions);
        var inputLabel = request.Mode == "clarification"
            ? "User answer"
            : "Revision instruction";

        return string.Join('\n',
            $"Mode: {request.Mode}",
            string.Empty,
            "Original user request:",
            request.OriginalRequest,
            string.Empty,
            "Current semantic plan (PRIMARY baseline):",
            currentPlan,
            string.Empty,
            "Clarification question:",
            request.ClarificationQuestion ?? "none",
            string.Empty,
            $"{inputLabel}:",
            request.UserInput,
            string.Empty,
            "Return a FULL semantic plan, never a delta.",
            request.Mode == "clarification"
                ? "Use the answer only to complete the missing or ambiguous field; preserve every field not explicitly changed. Do not interpret the answer as a standalone request."
                : "Override only fields explicitly changed by the revision instruction; preserve all other metric, groupBy, filters, date and ranking fields. Do not interpret the instruction as a standalone request.");
    }
}
