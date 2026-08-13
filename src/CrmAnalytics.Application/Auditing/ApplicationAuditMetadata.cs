using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CrmAnalytics.Application.SqlProduction;

namespace CrmAnalytics.Application.Auditing;

public sealed record ApplicationAuditMetadata
{
    public const string CurrentSchemaVersion =
        "application-audit-metadata-v1";
    public const string DwhContractVersion =
        "fabric-dwh-object-mapping-v1";
    public const string OltpContractVersion =
        "fabric-oltp-operational-orders-v1";

    [JsonConstructor]
    public ApplicationAuditMetadata(
        string schemaVersion,
        string conversationId,
        string sqlContractVersion,
        string physicalObject,
        string queryFingerprint,
        int timeoutSeconds,
        int rowLimit,
        int attemptNumber,
        string? deliveryId = null)
    {
        if (!string.Equals(schemaVersion, CurrentSchemaVersion,
                StringComparison.Ordinal))
        {
            throw new NotSupportedException(
                $"Audit metadata schema '{schemaVersion}' is not supported.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sqlContractVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(physicalObject);
        ArgumentException.ThrowIfNullOrWhiteSpace(queryFingerprint);
        if (conversationId.Trim().Length > 512)
        {
            throw new ArgumentException(
                "The conversation identifier cannot exceed 512 characters.",
                nameof(conversationId));
        }

        var normalizedContractVersion = sqlContractVersion.Trim();
        var normalizedPhysicalObject = physicalObject.Trim();
        if (normalizedContractVersion == OltpContractVersion)
        {
            if (!string.Equals(normalizedPhysicalObject,
                    "dbo.vw_operational_orders",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "The OLTP audit object is outside its contract.",
                    nameof(physicalObject));
            }
        }
        else if (normalizedContractVersion == DwhContractVersion)
        {
            if (!normalizedPhysicalObject.StartsWith(
                    "mart.", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "The DWH audit object is outside its contract.",
                    nameof(physicalObject));
            }
        }
        else
        {
            throw new NotSupportedException(
                $"SQL contract '{normalizedContractVersion}' is not supported.");
        }

        if (queryFingerprint.Length != 64
            || !queryFingerprint.All(Uri.IsHexDigit))
        {
            throw new ArgumentException(
                "The query fingerprint must be a SHA-256 hex value.",
                nameof(queryFingerprint));
        }

        if (timeoutSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
        }

        if (rowLimit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rowLimit));
        }

        if (attemptNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(attemptNumber));
        }

        if (deliveryId?.Trim().Length > 256)
        {
            throw new ArgumentException(
                "The delivery identifier cannot exceed 256 characters.",
                nameof(deliveryId));
        }

        SchemaVersion = schemaVersion;
        ConversationId = conversationId.Trim();
        SqlContractVersion = normalizedContractVersion;
        PhysicalObject = normalizedPhysicalObject;
        QueryFingerprint = queryFingerprint.ToLowerInvariant();
        TimeoutSeconds = timeoutSeconds;
        RowLimit = rowLimit;
        AttemptNumber = attemptNumber;
        DeliveryId = string.IsNullOrWhiteSpace(deliveryId)
            ? null
            : deliveryId.Trim();
    }

    public string SchemaVersion { get; }
    public string ConversationId { get; }
    public string SqlContractVersion { get; }
    public string PhysicalObject { get; }
    public string QueryFingerprint { get; }
    public int TimeoutSeconds { get; }
    public int RowLimit { get; }
    public int AttemptNumber { get; }
    public string? DeliveryId { get; }

    public static ApplicationAuditMetadata Create(
        string conversationId,
        SqlExecutionPlan executionPlan,
        int attemptNumber,
        string? deliveryId = null)
    {
        ArgumentNullException.ThrowIfNull(executionPlan);
        var contractVersion = executionPlan.Source switch
        {
            SqlDataSource.Dwh => DwhContractVersion,
            SqlDataSource.Oltp => OltpContractVersion,
            _ => throw new InvalidOperationException(
                "Audit metadata requires a known SQL source.")
        };

        if (string.IsNullOrWhiteSpace(
                executionPlan.VerifiedPhysicalObject))
        {
            throw new InvalidOperationException(
                "Audit metadata requires a verified physical object.");
        }

        if (executionPlan.RowLimit is not > 0)
        {
            throw new InvalidOperationException(
                "Audit metadata requires a positive row limit.");
        }

        return new ApplicationAuditMetadata(
            CurrentSchemaVersion,
            conversationId,
            contractVersion,
            executionPlan.VerifiedPhysicalObject,
            ApplicationAuditQueryFingerprint.Create(executionPlan.Sql),
            executionPlan.CommandTimeoutSeconds,
            executionPlan.RowLimit.Value,
            attemptNumber,
            deliveryId);
    }
}

public static class ApplicationAuditMetadataSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static string Serialize(ApplicationAuditMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        return JsonSerializer.Serialize(metadata, Options);
    }

    public static ApplicationAuditMetadata Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        return JsonSerializer.Deserialize<ApplicationAuditMetadata>(
                json, Options)
            ?? throw new JsonException("Audit metadata is empty.");
    }
}

public static partial class ApplicationAuditQueryFingerprint
{
    public static string Create(string parameterizedSql)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parameterizedSql);
        var parameterOrdinals = new Dictionary<string, int>(
            StringComparer.OrdinalIgnoreCase);
        var normalized = WhitespaceRegex().Replace(
            parameterizedSql.Trim(), " ");
        normalized = ParameterRegex().Replace(normalized, match =>
        {
            if (!parameterOrdinals.TryGetValue(match.Value, out var ordinal))
            {
                ordinal = parameterOrdinals.Count;
                parameterOrdinals.Add(match.Value, ordinal);
            }

            return $"@p{ordinal}";
        });
        normalized = normalized.ToUpperInvariant();
        return Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(normalized)))
            .ToLowerInvariant();
    }

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"@[A-Za-z_][A-Za-z0-9_]*",
        RegexOptions.CultureInvariant)]
    private static partial Regex ParameterRegex();
}
