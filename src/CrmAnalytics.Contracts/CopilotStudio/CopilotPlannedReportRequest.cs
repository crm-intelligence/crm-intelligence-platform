using System.ComponentModel.DataAnnotations;

namespace CrmAnalytics.Contracts.CopilotStudio;

public sealed class CopilotPlannedReportRequest
{
    [Required]
    [StringLength(2000, MinimumLength = 3)]
    public required string Prompt { get; init; }

    [Required]
    [StringLength(256, MinimumLength = 1)]
    public required string ConversationId { get; init; }

    [StringLength(64)]
    public string? PreviousRequestId { get; init; }

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

public sealed class CopilotSemanticIntent : IValidatableObject
{
    [StringLength(64)]
    public string? Metric { get; init; }

    [Required]
    [MaxLength(4)]
    public required IReadOnlyList<string?> GroupBy { get; init; }

    [Required]
    [MaxLength(16)]
    public required IReadOnlyList<CopilotSemanticFilter> Filters { get; init; }

    [Required]
    public required CopilotDateIntent Date { get; init; }

    [Required]
    public required CopilotRankingIntent Ranking { get; init; }

    public IEnumerable<ValidationResult> Validate(
        ValidationContext validationContext)
    {
        if (GroupBy?.Any(value => value?.Length > 64) == true)
        {
            yield return new ValidationResult(
                "Group-by keys can be at most 64 characters.",
                [nameof(GroupBy)]);
        }
    }
}

public sealed class CopilotSemanticFilter : IValidatableObject
{
    [StringLength(64)]
    public string? Dimension { get; init; }

    [StringLength(16)]
    public string? Operator { get; init; }

    [Required]
    [MaxLength(20)]
    public required IReadOnlyList<string?> Values { get; init; }

    public IEnumerable<ValidationResult> Validate(
        ValidationContext validationContext)
    {
        if (Values?.Any(value => value?.Length > 512) == true)
        {
            yield return new ValidationResult(
                "Filter values can be at most 512 characters.",
                [nameof(Values)]);
        }
    }
}

public sealed class CopilotDateIntent
{
    [StringLength(16)]
    public string? Kind { get; init; }

    [StringLength(32)]
    public string? RelativeExpression { get; init; }

    public int Count { get; init; }

    [StringLength(10)]
    public string? From { get; init; }

    [StringLength(10)]
    public string? To { get; init; }

    [StringLength(16)]
    public string? Grain { get; init; }
}

public sealed class CopilotRankingIntent
{
    public int TopN { get; init; }

    [StringLength(64)]
    public string? OrderBy { get; init; }

    [StringLength(8)]
    public string? Direction { get; init; }
}

public sealed class CopilotUnresolvedConcept
{
    [StringLength(16)]
    public string? Kind { get; init; }
}

public sealed class CopilotClarification
{
    [StringLength(16)]
    public string? Kind { get; init; }
}
