using CrmAnalytics.Domain.Conversations;
using CrmAnalytics.Domain.ReportRequests;
using CrmAnalytics.Infrastructure.Persistence.SqlServer;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace CrmAnalytics.IntegrationTests;

public sealed class PersistenceModelTests
{
    private readonly IModel _model = CreateContext().Model;

    [Fact]
    public void ReportRequestMapping_ContainsRequiredSchemaColumnsAndKey()
    {
        var entity = _model.FindEntityType(typeof(ReportRequest));
        Assert.NotNull(entity);

        Assert.Equal("ReportRequests", entity!.GetTableName());
        Assert.Equal("crm", entity.GetSchema());
        Assert.Equal(
            nameof(ReportRequest.RequestId),
            Assert.Single(entity.FindPrimaryKey()!.Properties).Name);

        Assert.Equal(
            2000,
            entity.FindProperty(nameof(ReportRequest.Prompt))!
                .GetMaxLength());
        Assert.Equal(
            "date",
            entity.FindProperty(nameof(ReportRequest.ReferenceDate))!
                .GetColumnType());
        Assert.Equal(
            "nvarchar(max)",
            entity.FindProperty(
                    nameof(ReportRequest.CanonicalRequestJson))!
                .GetColumnType());
        Assert.Equal(
            128,
            entity.FindProperty(nameof(ReportRequest.RejectionCode))!
                .GetMaxLength());
        Assert.Equal(
            1000,
            entity.FindProperty(nameof(ReportRequest.RejectionMessage))!
                .GetMaxLength());

        AssertRequiredIdentityAndTimestamps(entity);
    }

    [Fact]
    public void ConversationMapping_ContainsUniqueTeamsIdentityAndColumns()
    {
        var entity = _model.FindEntityType(typeof(Conversation));
        Assert.NotNull(entity);

        Assert.Equal("Conversations", entity!.GetTableName());
        Assert.Equal("crm", entity.GetSchema());

        var teamsIndex = Assert.Single(
            entity.GetIndexes(),
            index => index.Properties.Select(property => property.Name)
                .SequenceEqual(
                    new[] { nameof(Conversation.TeamsConversationId) }));
        Assert.True(teamsIndex.IsUnique);

        AssertRequiredIdentityAndTimestamps(entity);
    }

    [Fact]
    public void Status_UsesStrictStringConversion_AndRejectedRoundTrips()
    {
        var property = _model
            .FindEntityType(typeof(ReportRequest))!
            .FindProperty(nameof(ReportRequest.Status))!;
        var converter = property.GetTypeMapping().Converter;
        Assert.NotNull(converter);

        Assert.Equal(
            "Rejected",
            converter!.ConvertToProvider(
                ReportRequestStatus.Rejected));
        Assert.Equal(
            ReportRequestStatus.Rejected,
            converter.ConvertFromProvider("Rejected"));
        Assert.ThrowsAny<Exception>(
            (Action)(() =>
                converter.ConvertFromProvider(
                    "UnknownDatabaseStatus")));
    }

    [Fact]
    public void RowVersions_AreGeneratedConcurrencyTokens()
    {
        AssertRowVersion(
            _model.FindEntityType(typeof(ReportRequest))!);
        AssertRowVersion(
            _model.FindEntityType(typeof(Conversation))!);
    }

    [Fact]
    public void RequiredIndexesExist_AndNoCascadeRelationshipsExist()
    {
        var report = _model.FindEntityType(typeof(ReportRequest))!;
        AssertIndex(report, "TenantId", "UserId", "RequestId");
        AssertIndex(
            report,
            "TenantId",
            "UserId",
            "ConversationId",
            "CreatedAt");
        AssertIndex(report, "ConversationId", "CreatedAt");
        AssertIndex(report, "PreviousRequestId");
        AssertIndex(report, "Status", "UpdatedAt");

        var conversation = _model.FindEntityType(typeof(Conversation))!;
        AssertIndex(
            conversation,
            "TenantId",
            "UserId",
            "TeamsConversationId");
        AssertIndex(conversation, "LastRequestId");
        AssertIndex(
            conversation,
            "TenantId",
            "UserId",
            "UpdatedAt");

        Assert.Empty(report.GetForeignKeys());
        Assert.Empty(conversation.GetForeignKeys());
    }

    private static CrmAnalyticsDbContext CreateContext()
    {
        var options =
            new DbContextOptionsBuilder<CrmAnalyticsDbContext>()
                .UseSqlServer(
                    "Server=(localdb)\\mssqllocaldb;"
                    + "Database=CrmAnalyticsModelTests;"
                    + "Trusted_Connection=True")
                .Options;

        return new CrmAnalyticsDbContext(options);
    }

    private static void AssertRequiredIdentityAndTimestamps(
        IEntityType entity)
    {
        Assert.False(entity.FindProperty("UserId")!.IsNullable);
        Assert.False(entity.FindProperty("TenantId")!.IsNullable);
        Assert.Equal(
            "datetimeoffset(7)",
            entity.FindProperty("CreatedAt")!.GetColumnType());
        Assert.Equal(
            "datetimeoffset(7)",
            entity.FindProperty("UpdatedAt")!.GetColumnType());
    }

    private static void AssertRowVersion(IEntityType entity)
    {
        var rowVersion = entity.FindProperty("RowVersion");

        Assert.NotNull(rowVersion);
        Assert.True(rowVersion!.IsConcurrencyToken);
        Assert.Equal(
            ValueGenerated.OnAddOrUpdate,
            rowVersion.ValueGenerated);
        Assert.Equal("rowversion", rowVersion.GetColumnType());
    }

    private static void AssertIndex(
        IEntityType entity,
        params string[] propertyNames)
    {
        Assert.Contains(
            entity.GetIndexes(),
            index => index.Properties
                .Select(property => property.Name)
                .SequenceEqual(propertyNames));
    }
}
