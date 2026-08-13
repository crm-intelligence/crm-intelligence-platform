namespace crm_project.Models;

public class AuditLogEntry
{
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

    public string RequestId { get; set; } = string.Empty;

    public string UserId { get; set; } = string.Empty;

    public string Decision { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    public string Source { get; set; } = string.Empty;

    public string Query { get; set; } = string.Empty;

    public string TargetTable { get; set; } = string.Empty;

    public string Region { get; set; } = string.Empty;
}