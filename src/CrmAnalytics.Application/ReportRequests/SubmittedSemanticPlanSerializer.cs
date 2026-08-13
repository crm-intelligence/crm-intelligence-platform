using System.Text.Json;
using System.Text.Json.Serialization;
using CrmAnalytics.Contracts.Integrations;

namespace CrmAnalytics.Application.ReportRequests;

internal static class SubmittedSemanticPlanSerializer
{
    private static readonly JsonSerializerOptions Options = new(
        JsonSerializerDefaults.Web)
    {
        MaxDepth = 32,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static string Serialize(SubmittedSemanticPlanningResult plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return JsonSerializer.Serialize(plan, Options);
    }

    public static SubmittedSemanticPlanningResult Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        return JsonSerializer.Deserialize<SubmittedSemanticPlanningResult>(
                json, Options)
            ?? throw new JsonException(
                "Submitted semantic plan JSON resolved to null.");
    }
}
