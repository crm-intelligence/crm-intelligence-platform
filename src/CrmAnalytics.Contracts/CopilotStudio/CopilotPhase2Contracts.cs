using System.ComponentModel.DataAnnotations;
using CrmAnalytics.Contracts.Integrations;

namespace CrmAnalytics.Contracts.CopilotStudio;

public sealed class CopilotPlannedRevisionRequest
{
    [Required]
    [StringLength(2000, MinimumLength = 3)]
    public required string RevisionInstruction { get; init; }

    [Required]
    public required CopilotSemanticPlan Plan { get; init; }
}

public sealed class CopilotPlannedClarificationRequest
{
    [Required]
    [StringLength(2000, MinimumLength = 3)]
    public required string Answer { get; init; }

    [Required]
    public required CopilotSemanticPlan Plan { get; init; }
}

public sealed class CopilotSemanticPlan
{
    [StringLength(32)]
    public string? Outcome { get; init; }

    [Required]
    public required CopilotSemanticIntent SemanticIntent { get; init; }

    [Required]
    [MaxLength(5)]
    public required IReadOnlyList<CopilotUnresolvedConcept>
        UnresolvedConcepts { get; init; }

    [Required]
    public required CopilotClarification Clarification { get; init; }
}

public sealed record ReportPlanningContextResponse(
    string RequestId,
    string ConversationId,
    string OriginalRequest,
    string Status,
    string? ClarificationQuestion,
    SubmittedSemanticPlanningResult? CurrentSemanticPlan);
