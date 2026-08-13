using crm_project.Models;

namespace crm_project.Services;

public class AuditLogService
{
    private readonly ILogger<AuditLogService> _logger;

    public AuditLogService(ILogger<AuditLogService> logger)
    {
        _logger = logger;
    }

    public void Write(AuditLogEntry entry)
    {
        _logger.LogInformation(
            "AUDIT | TimestampUtc: {TimestampUtc} | RequestId: {RequestId} | UserId: {UserId} | Decision: {Decision} | Reason: {Reason} | Source: {Source} | Query: {Query} | TargetTable: {TargetTable} | Region: {Region}",
            entry.TimestampUtc,
            entry.RequestId,
            entry.UserId,
            entry.Decision,
            entry.Reason,
            entry.Source,
            entry.Query,
            entry.TargetTable,
            entry.Region);
    }
}