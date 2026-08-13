using CrmAnalytics.Application.Abstractions.Persistence;
using CrmAnalytics.Application.Auditing;
using CrmAnalytics.Application.Identity;
using CrmAnalytics.Infrastructure.Auditing;
using CrmAnalytics.Infrastructure.Identity;
using CrmAnalytics.Infrastructure.Persistence;
using CrmAnalytics.Infrastructure.Persistence.SqlServer;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CrmAnalytics.IntegrationTests;

public sealed class PhaseSevenTwoModelAndProviderTests
{
    [Fact]
    public void SqlModel_ContainsAuditIndexesAndAssignmentCascadeOnly()
    {
        using var context = CreateModelContext();
        var model = context.GetService<IDesignTimeModel>().Model;
        var audit = model.GetEntityTypes().Single(value =>
            value.GetTableName() == "ApplicationAuditEvents");
        Assert.Equal("crm", audit.GetSchema());
        Assert.Equal("EventId",
            Assert.Single(audit.FindPrimaryKey()!.Properties).Name);
        Assert.Equal(typeof(string), audit.FindProperty("EventType")!.ClrType);
        Assert.Equal(typeof(string), audit.FindProperty("Outcome")!.ClrType);
        AssertIndex(audit, "RequestId", "OccurredAt");
        AssertIndex(audit, "TenantId", "ActorUserId", "OccurredAt");
        Assert.Null(audit.FindProperty("Prompt"));
        Assert.Null(audit.FindProperty("CanonicalRequestJson"));
        Assert.Null(audit.FindProperty("RejectionMessage"));
        Assert.Equal(32,
            audit.FindProperty("DataSource")!.GetMaxLength());
        Assert.True(audit.FindProperty("DataSource")!.IsNullable);
        Assert.True(audit.FindProperty("DurationMilliseconds")!.IsNullable);
        Assert.True(audit.FindProperty("RowCount")!.IsNullable);
        Assert.True(audit.FindProperty("ResultTruncated")!.IsNullable);
        Assert.True(audit.FindProperty("AuditMetadataJson")!.IsNullable);
        Assert.Equal("nvarchar(max)",
            audit.FindProperty("AuditMetadataJson")!.GetColumnType());
        Assert.Contains(audit.GetCheckConstraints(), constraint =>
            constraint.Name
                == "CK_ApplicationAuditEvents_AuditMetadataJson_IsJson"
            && constraint.Sql.Contains("ISJSON", StringComparison.Ordinal));
        Assert.Null(audit.FindProperty("Sql"));
        Assert.Null(audit.FindProperty("Parameters"));
        Assert.Null(audit.FindProperty("Columns"));
        Assert.Null(audit.FindProperty("Rows"));
        Assert.Null(audit.FindProperty("AppliedScopeFilter"));
        Assert.Null(audit.FindProperty("ResultReference"));

        var assignment = model.GetEntityTypes().Single(value =>
            value.GetTableName() == "UserDataAccessAssignments");
        var rowVersion = assignment.FindProperty("RowVersion")!;
        Assert.True(rowVersion.IsConcurrencyToken);
        Assert.Equal("rowversion", rowVersion.GetColumnType());
        AssertIndex(assignment, "IsActive", "TenantId");

        foreach (var childTable in new[]
                 { "UserDataAccessRegions", "UserDataAccessStores" })
        {
            var child = model.GetEntityTypes().Single(value =>
                value.GetTableName() == childTable);
            Assert.Equal(Microsoft.EntityFrameworkCore.DeleteBehavior.Cascade,
                Assert.Single(child.GetForeignKeys()).DeleteBehavior);
        }
        Assert.Empty(audit.GetForeignKeys());
    }

    [Theory]
    [InlineData("Development", "Configuration", "InMemory", true)]
    [InlineData("Development", "SqlServer", "SqlServer", true)]
    [InlineData("Production", "Configuration", "SqlServer", false)]
    [InlineData("Production", "SqlServer", "SqlServer", true)]
    [InlineData("Development", "SqlServer", "InMemory", false)]
    [InlineData("Development", "Unknown", "InMemory", false)]
    public void ProviderCombinations_AreValidated(
        string environmentName,
        string dataProvider,
        string persistenceProvider,
        bool expected)
    {
        var configuration = Configuration(dataProvider,
            persistenceProvider);
        var validator = new ReportDataAccessOptionsValidator(
            configuration,
            new TestEnvironment { EnvironmentName = environmentName });
        var result = validator.Validate(Options.DefaultName,
            new ReportDataAccessOptions
            {
                Provider = dataProvider,
                Assignments = []
            });
        Assert.Equal(expected, result.Succeeded);
    }

    [Fact]
    public void SqlRegistrations_ShareOneScopedDbContext()
    {
        var configuration = Configuration("SqlServer", "SqlServer");
        var environment = new TestEnvironment
        {
            EnvironmentName = Environments.Production
        };
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHostEnvironment>(environment);
        services.AddCrmAnalyticsPersistence(configuration, environment);
        services.AddReportDataAccess(configuration, environment);
        using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider
            .GetRequiredService<CrmAnalyticsDbContext>();
        var store = scope.ServiceProvider
            .GetRequiredService<IUserDataAccessAssignmentStore>();
        var writer = scope.ServiceProvider
            .GetRequiredService<IApplicationAuditWriter>();
        var runner = scope.ServiceProvider
            .GetRequiredService<IApplicationTransactionRunner>();

        Assert.IsType<SqlUserDataAccessAssignmentStore>(store);
        Assert.IsType<SqlApplicationAuditWriter>(writer);
        Assert.IsType<EfCoreApplicationTransactionRunner>(runner);
        Assert.Same(context, ContextOf(store));
        Assert.Same(context, ContextOf(writer));
        Assert.Same(context, ContextOf(runner));
    }

    [Fact]
    public void InMemoryRegistrations_UseDevelopmentImplementations()
    {
        var configuration = Configuration("Configuration", "InMemory");
        var environment = new TestEnvironment();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHostEnvironment>(environment);
        services.AddCrmAnalyticsPersistence(configuration, environment);
        services.AddReportDataAccess(configuration, environment);
        using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });
        using var scope = provider.CreateScope();

        Assert.IsType<InMemoryApplicationTransactionRunner>(
            scope.ServiceProvider
                .GetRequiredService<IApplicationTransactionRunner>());
        Assert.IsType<InMemoryApplicationAuditWriter>(
            scope.ServiceProvider
                .GetRequiredService<IApplicationAuditWriter>());
        Assert.IsType<ConfigurationUserDataAccessAssignmentStore>(
            scope.ServiceProvider
                .GetRequiredService<IUserDataAccessAssignmentStore>());
    }

    private static object? ContextOf(object service) =>
        service.GetType().GetField("_dbContext",
            System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.NonPublic)?.GetValue(service);

    private static IConfiguration Configuration(
        string dataProvider,
        string persistenceProvider) =>
        new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ReportDataAccess:Provider"] = dataProvider,
                ["Persistence:Provider"] = persistenceProvider,
                ["ConnectionStrings:CrmAnalytics"] =
                    "Server=localhost;Database=placeholder;User Id=test;"
                    + "Password=placeholder;TrustServerCertificate=True"
            }).Build();

    private static CrmAnalyticsDbContext CreateModelContext() => new(
        new DbContextOptionsBuilder<CrmAnalyticsDbContext>()
            .UseSqlServer(
                "Server=(localdb)\\mssqllocaldb;Database=ModelOnly;"
                + "Trusted_Connection=True")
            .Options);

    private static void AssertIndex(
        Microsoft.EntityFrameworkCore.Metadata.IEntityType entity,
        params string[] names) => Assert.Contains(entity.GetIndexes(),
        index => index.Properties.Select(value => value.Name)
            .SequenceEqual(names));

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } =
            Environments.Development;
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }
}
