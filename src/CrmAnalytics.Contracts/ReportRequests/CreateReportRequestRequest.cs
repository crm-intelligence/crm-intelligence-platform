using System.ComponentModel.DataAnnotations;

namespace CrmAnalytics.Contracts.ReportRequests;

public sealed class CreateReportRequestRequest {
    [Required]
    [StringLength(
        maximumLength: 2000,
        MinimumLength = 3,
        ErrorMessage = "Prompt 3 ile 2000 karakter arasında olmalıdır.")]
    public required string Prompt { get; init; }

    [Required]
    [StringLength(
        maximumLength: 256,
        MinimumLength = 1,
        ErrorMessage = "Conversation ID en fazla 256 karakter olabilir.")]
    public required string ConversationId { get; init; }

    [StringLength(
        maximumLength: 64,
        ErrorMessage = "Previous Request ID en fazla 64 karakter olabilir.")]
    public string? PreviousRequestId { get; init; }
}