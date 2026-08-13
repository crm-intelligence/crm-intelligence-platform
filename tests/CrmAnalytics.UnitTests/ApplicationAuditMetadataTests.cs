using System.Text.Json;
using CrmAnalytics.Application.Auditing;
using CrmAnalytics.Application.SqlProduction;

namespace CrmAnalytics.UnitTests;

public sealed class ApplicationAuditMetadataTests
{
    [Theory]
    [InlineData(SqlDataSource.Dwh, "mart.vw_sales", 30, 5000,
        ApplicationAuditMetadata.DwhContractVersion)]
    [InlineData(SqlDataSource.Oltp, "dbo.vw_operational_orders", 15, 1000,
        ApplicationAuditMetadata.OltpContractVersion)]
    public void Source_MapsExplicitContractAndExecutionBoundaries(
        SqlDataSource source,
        string physicalObject,
        int timeoutSeconds,
        int rowLimit,
        string expectedContract)
    {
        var metadata = ApplicationAuditMetadata.Create(
            "conversation-1",
            Plan(source, physicalObject, timeoutSeconds, rowLimit, "SP"),
            2,
            "delivery-7");

        Assert.Equal(ApplicationAuditMetadata.CurrentSchemaVersion,
            metadata.SchemaVersion);
        Assert.Equal(expectedContract, metadata.SqlContractVersion);
        Assert.Equal(physicalObject, metadata.PhysicalObject);
        Assert.Equal(timeoutSeconds, metadata.TimeoutSeconds);
        Assert.Equal(rowLimit, metadata.RowLimit);
        Assert.Equal(2, metadata.AttemptNumber);
        Assert.Equal("delivery-7", metadata.DeliveryId);
    }

    [Fact]
    public void UnknownSourceAndUnverifiedObject_FailClosed()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Plan(
            SqlDataSource.Unknown,
            "mart.vw_sales",
            30,
            5000,
            "SP"));
        var unverified = new SqlExecutionPlan(
            SqlDataSource.Dwh,
            "SELECT amount FROM mart.vw_sales WHERE region = @region",
            [],
            "region = @region",
            30,
            null,
            verifiedPhysicalObject: null,
            rowLimit: 5000);
        Assert.Throws<InvalidOperationException>(() =>
            ApplicationAuditMetadata.Create(
                "conversation-1", unverified, 1));
    }

    [Fact]
    public void Fingerprint_IsValueIndependentAndShapeSensitive()
    {
        var first = ApplicationAuditMetadata.Create(
            "conversation-1",
            Plan(SqlDataSource.Dwh, "mart.vw_sales", 30, 5000, "SP"),
            1);
        var second = ApplicationAuditMetadata.Create(
            "conversation-1",
            Plan(SqlDataSource.Dwh, "mart.vw_sales", 30, 5000, "RJ"),
            1);
        var different = new SqlExecutionPlan(
            SqlDataSource.Dwh,
            "SELECT COUNT(*) FROM mart.vw_sales WHERE region = @state",
            [new("@state", SqlExecutionParameterKind.Text, "SP", true)],
            "region = @state",
            30,
            null,
            "mart.vw_sales",
            5000);

        Assert.Equal(first.QueryFingerprint, second.QueryFingerprint);
        Assert.NotEqual(first.QueryFingerprint,
            ApplicationAuditMetadata.Create(
                "conversation-1", different, 1).QueryFingerprint);
    }

    [Fact]
    public void Json_IsVersionedValidAndContainsNoSensitivePayload()
    {
        const string parameterValue = "secret-region-value";
        var metadata = ApplicationAuditMetadata.Create(
            "conversation-preserved",
            Plan(SqlDataSource.Oltp, "dbo.vw_operational_orders",
                15, 1000, parameterValue),
            3,
            "message-42");
        var json = ApplicationAuditMetadataSerializer.Serialize(metadata);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("application-audit-metadata-v1",
            root.GetProperty("schemaVersion").GetString());
        Assert.Equal("conversation-preserved",
            root.GetProperty("conversationId").GetString());
        Assert.Equal(3, root.GetProperty("attemptNumber").GetInt32());
        Assert.Equal("message-42",
            root.GetProperty("deliveryId").GetString());
        Assert.False(root.TryGetProperty("source", out _));
        Assert.False(root.TryGetProperty("sql", out _));
        Assert.False(root.TryGetProperty("parameters", out _));
        Assert.DoesNotContain(parameterValue, json, StringComparison.Ordinal);
        Assert.Equal(metadata,
            ApplicationAuditMetadataSerializer.Deserialize(json));
    }

    [Fact]
    public void PhysicalObject_ComesOnlyFromVerifiedPlanField()
    {
        const string userText = "FROM attacker.injected_table";
        var metadata = ApplicationAuditMetadata.Create(
            "conversation-1",
            Plan(SqlDataSource.Dwh, "mart.vw_payment", 30, 5000,
                userText),
            1);

        Assert.Equal("mart.vw_payment", metadata.PhysicalObject);
        Assert.DoesNotContain("attacker", metadata.PhysicalObject,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(userText,
            ApplicationAuditMetadataSerializer.Serialize(metadata),
            StringComparison.Ordinal);
    }

    private static SqlExecutionPlan Plan(
        SqlDataSource source,
        string physicalObject,
        int timeoutSeconds,
        int rowLimit,
        string parameterValue) => new(
            source,
            "SELECT amount FROM mart.vw_sales WHERE region = @region",
            [new("@region", SqlExecutionParameterKind.Text,
                parameterValue, true)],
            "region = @region",
            timeoutSeconds,
            null,
            physicalObject,
            rowLimit);
}
