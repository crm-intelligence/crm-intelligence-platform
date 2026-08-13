using System.ComponentModel.DataAnnotations;

namespace CrmAnalytics.Contracts.ReportRequests;

public sealed class ReviseReportRequestRequest
{
    [Required]
    [StringLength(
        maximumLength: 2000,
        MinimumLength = 3,
        ErrorMessage = "Prompt 3 ile 2000 karakter arasında olmalıdır.")]
    public required string Prompt { get; init; }
}
