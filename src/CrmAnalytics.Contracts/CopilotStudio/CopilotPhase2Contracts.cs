using System.ComponentModel.DataAnnotations;
using CrmAnalytics.Contracts.Integrations;
using CrmAnalytics.Contracts.SqlAgent;

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

public sealed class CopilotRoutedPlannedReportRequest
{
    [Required]
    [StringLength(2000, MinimumLength = 3)]
    public required string Prompt { get; init; }

    [Required]
    [StringLength(256, MinimumLength = 1)]
    public required string ConversationId { get; init; }

    [StringLength(64)]
    public string? PreviousRequestId { get; init; }

    [Required]
    public required CopilotSemanticPlan Plan { get; init; }

    [Required]
    public required CopilotSqlAgentIntent Intent { get; init; }
}

public sealed class CopilotRoutedPlannedRevisionRequest
{
    [Required]
    [StringLength(2000, MinimumLength = 3)]
    public required string RevisionInstruction { get; init; }

    [Required]
    public required CopilotSemanticPlan Plan { get; init; }

    [Required]
    public required CopilotSqlAgentIntent Intent { get; init; }
}

public sealed class CopilotRoutedPlannedClarificationRequest
{
    [Required]
    [StringLength(2000, MinimumLength = 3)]
    public required string Answer { get; init; }

    [Required]
    public required CopilotSemanticPlan Plan { get; init; }

    [Required]
    public required CopilotSqlAgentIntent Intent { get; init; }
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

public sealed record CopilotRoutedReportResponse(
    string RequestId,
    string RequestStatus,
    string? PreviousRequestId,
    string? ConversationId,
    SqlAgentCapabilityResponse Capability);
