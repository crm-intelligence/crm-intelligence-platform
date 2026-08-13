using System.Security.Cryptography;
using System.Text;

namespace CrmAnalytics.Application.Auditing;

public static class ApplicationAuditEventFactory
{
    public static ApplicationAuditEvent Create(
        ApplicationAuditEventType eventType,
        ApplicationAuditOutcome outcome,
        DateTimeOffset occurredAt,
        string requestId,
        string? previousRequestId,
        string? correlationId,
        string? actorUserId,
        string? tenantId,
        string? reportStatus,
        string? reasonCode = null,
        string? dataSource = null,
        long? durationMilliseconds = null,
        int? rowCount = null,
        bool? resultTruncated = null,
        ApplicationAuditMetadata? auditMetadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        var utcOccurredAt = occurredAt.ToUniversalTime();
        var hashInput = string.Join(
            "\n",
            requestId.Trim(),
            eventType.ToString(),
            utcOccurredAt.UtcTicks.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            previousRequestId?.Trim() ?? string.Empty);
        var eventId = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(hashInput)))
            .ToLowerInvariant();

        return new ApplicationAuditEvent(
            eventId,
            eventType,
            outcome,
            utcOccurredAt,
            requestId,
            previousRequestId,
            correlationId,
            actorUserId,
            tenantId,
            reportStatus,
            reasonCode,
            dataSource,
            durationMilliseconds,
            rowCount,
            resultTruncated,
            auditMetadata);
    }
}
