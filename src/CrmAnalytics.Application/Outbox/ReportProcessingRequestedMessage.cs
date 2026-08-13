using CrmAnalytics.Contracts.Integrations;
using System.Text.Json.Serialization;

namespace CrmAnalytics.Application.Outbox;

public sealed record ReportProcessingRequestedMessage
{
    public const int LegacySchemaVersion = 1;
    public const int CurrentSchemaVersion = 2;

    public ReportProcessingRequestedMessage(
        string requestId,
        string correlationId,
        DateTimeOffset requestedAt,
        int schemaVersion = LegacySchemaVersion,
        SubmittedSemanticPlanningResult? semanticPlan = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        if (requestedAt.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "The requested time must be in UTC.",
                nameof(requestedAt));
        }

        if (!IsSupportedSchemaVersion(schemaVersion))
        {
            throw new NotSupportedException(
                $"Outbox schema version '{schemaVersion}' is not supported.");
        }
        if (schemaVersion == LegacySchemaVersion && semanticPlan is not null)
        {
            throw new ArgumentException(
                "Schema version 1 cannot carry a submitted semantic plan.",
                nameof(semanticPlan));
        }
        if (schemaVersion == CurrentSchemaVersion && semanticPlan is null)
        {
            throw new ArgumentException(
                "Schema version 2 requires a submitted semantic plan.",
                nameof(semanticPlan));
        }

        RequestId = requestId.Trim();
        CorrelationId = correlationId.Trim();
        RequestedAt = requestedAt;
        SchemaVersion = schemaVersion;
        SemanticPlan = semanticPlan;
    }

    public string RequestId { get; }
    public string CorrelationId { get; }
    public DateTimeOffset RequestedAt { get; }
    public int SchemaVersion { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SubmittedSemanticPlanningResult? SemanticPlan { get; }

    public static bool IsSupportedSchemaVersion(int schemaVersion) =>
        schemaVersion is LegacySchemaVersion or CurrentSchemaVersion;

    public override string ToString() =>
        $"ReportProcessingRequestedMessage {{ SchemaVersion = {SchemaVersion}, Redacted }}";
}
