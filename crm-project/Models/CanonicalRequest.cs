using System.ComponentModel.DataAnnotations;

namespace crm_project.Models;

public class CanonicalRequest
{
    [Required]
    public string RequestId { get; set; } = string.Empty;

    [Required]
    public string Prompt { get; set; } = string.Empty;

    [Required]
    public string UseCase { get; set; } = string.Empty;

    public Dictionary<string, object> Parameters { get; set; } = new();

    [Required]
    public string UserId { get; set; } = string.Empty;

    [Required]
    public string Source { get; set; } = "Teams";

    [Required]
    public string TargetTable { get; set; } = string.Empty;

    [Required]
    public string Region { get; set; } = string.Empty;
}