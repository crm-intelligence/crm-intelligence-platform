using System.ComponentModel.DataAnnotations;

namespace CrmAnalytics.Contracts.ReportRequests;

public sealed class SubmitReportClarificationRequest
{
    [Required]
    [StringLength(
        maximumLength: 2000,
        MinimumLength = 3,
        ErrorMessage = "Açıklama 3 ile 2000 karakter arasında olmalıdır.")]
    public required string Response { get; init; }
}
