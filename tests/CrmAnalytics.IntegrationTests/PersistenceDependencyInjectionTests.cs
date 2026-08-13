using CrmAnalytics.Application.Abstractions.Persistence;
using CrmAnalytics.Infrastructure.Persistence;
using CrmAnalytics.Infrastructure.Persistence.SqlServer;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CrmAnalytics.IntegrationTests;

public sealed class PersistenceDependencyInjectionTests
{
    [Fact]
    public void DevelopmentInMemory_ResolvesExistingSingletonRepositories()
    {
        using var provider = CreateProvider(
            Environments.Development,
            PersistenceOptions.InMemoryProvider);

        var report = provider.GetRequiredService<
            IReportRequestRepository>();
        var conversation = provider.GetRequiredService<
            IConversationRepository>();

        Assert.IsType<InMemoryReportRequestRepository>(report);
        Assert.IsType<InMemoryConversationRepository>(conversation);
        Assert.Same(
            report,
            provider.GetRequiredService<IReportRequestRepository>());
        Assert.DoesNotContain(
            provider.GetRequiredService<
                    IOptions<HealthCheckServiceOptions>>()
                .Value.Registrations,
            registration => registration.Name == "crm-database");
    }

    [Fact]
    public void SqlServer_UsesScopedRepositoriesAndSharedScopedDbContext()
    {
        using var provider = CreateProvider(
            Environments.Production,
            PersistenceOptions.SqlServerProvider);

        using var firstScope = provider.CreateScope();
        var firstContext = firstScope.ServiceProvider
            .GetRequiredService<CrmAnalyticsDbContext>();
        var report = firstScope.ServiceProvider
            .GetRequiredService<IReportRequestRepository>();
        var conversation = firstScope.ServiceProvider
            .GetRequiredService<IConversationRepository>();

        Assert.IsType<SqlReportRequestRepository>(report);
        Assert.IsType<SqlConversationRepository>(conversation);
        Assert.Same(firstContext, GetContext(report));
        Assert.Same(firstContext, GetContext(conversation));
        Assert.Same(
            report,
            firstScope.ServiceProvider.GetRequiredService<
                IReportRequestRepository>());

        using var secondScope = provider.CreateScope();
        Assert.NotSame(
            firstContext,
            secondScope.ServiceProvider
                .GetRequiredService<CrmAnalyticsDbContext>());

        var options = firstContext.GetService<IDbContextOptions>();
        Assert.False(
            options.Extensions
                .OfType<CoreOptionsExtension>()
                .Single()
                .IsSensitiveDataLoggingEnabled);
        Assert.Equal(
            "Microsoft.EntityFrameworkCore.SqlServer",
            firstContext.Database.ProviderName);
        Assert.Equal(
            30,
            firstContext.Database.GetCommandTimeout());

        Assert.Contains(
            provider.GetRequiredService<
                    IOptions<HealthCheckServiceOptions>>()
                .Value.Registrations,
            registration =>
                registration.Name == "crm-database"
                && registration.Tags.Contains("ready"));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void InMemoryOutsideDevelopmentOrTest_IsRejected(
        string environmentName)
    {
        var (configuration, environment) = CreateInputs(
            environmentName,
            PersistenceOptions.InMemoryProvider,
            connectionString: null);
        var validator = new PersistenceOptionsValidator(
            configuration,
            environment);

        var result = validator.Validate(
            Options.DefaultName,
            new PersistenceOptions
            {
                Provider = PersistenceOptions.InMemoryProvider
            });

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void SqlServerWithoutConnectionString_IsRejected()
    {
        var (configuration, environment) = CreateInputs(
            Environments.Production,
            PersistenceOptions.SqlServerProvider,
            connectionString: string.Empty);
        var validator = new PersistenceOptionsValidator(
            configuration,
            environment);

        var result = validator.Validate(
            Options.DefaultName,
            new PersistenceOptions
            {
                Provider = PersistenceOptions.SqlServerProvider
            });

        Assert.False(result.Succeeded);
    }

    [Theory]
    [InlineData(0, 5, 30)]
    [InlineData(301, 5, 30)]
    [InlineData(30, -1, 30)]
    [InlineData(30, 11, 30)]
    [InlineData(30, 5, 0)]
    [InlineData(30, 5, 121)]
    public void InvalidSqlServerRanges_AreRejected(
        int timeout,
        int retryCount,
        int retryDelay)
    {
        var (configuration, environment) = CreateInputs(
            Environments.Production,
            PersistenceOptions.SqlServerProvider);
        var validator = new PersistenceOptionsValidator(
            configuration,
            environment);

        var result = validator.Validate(
            Options.DefaultName,
            new PersistenceOptions
            {
                Provider = PersistenceOptions.SqlServerProvider,
                SqlServer = new SqlServerPersistenceOptions
                {
                    CommandTimeoutSeconds = timeout,
                    MaxRetryCount = retryCount,
                    MaxRetryDelaySeconds = retryDelay
                }
            });

        Assert.False(result.Succeeded);
    }

    private static ServiceProvider CreateProvider(
        string environmentName,
        string persistenceProvider)
    {
        var (configuration, environment) = CreateInputs(
            environmentName,
            persistenceProvider);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHostEnvironment>(environment);
        services.AddCrmAnalyticsPersistence(
            configuration,
            environment);

        return services.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });
    }

    private static (
        IConfiguration Configuration,
        IHostEnvironment Environment) CreateInputs(
            string environmentName,
            string provider,
            string? connectionString =
                "Server=localhost;Database=CrmAnalyticsTests;"
                + "User Id=test;Password=placeholder;"
                + "TrustServerCertificate=True")
    {
        var values = new Dictionary<string, string?>
        {
            ["Persistence:Provider"] = provider,
            ["Persistence:SqlServer:CommandTimeoutSeconds"] = "30",
            ["Persistence:SqlServer:EnableRetryOnFailure"] = "true",
            ["Persistence:SqlServer:MaxRetryCount"] = "5",
            ["Persistence:SqlServer:MaxRetryDelaySeconds"] = "30",
            ["ConnectionStrings:CrmAnalytics"] = connectionString
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        var environment = new TestHostEnvironment
        {
            EnvironmentName = environmentName
        };

        return (configuration, environment);
    }

    private static CrmAnalyticsDbContext GetContext(object repository)
    {
        var field = repository.GetType().GetField(
            "_dbContext",
            System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic);

        return Assert.IsType<CrmAnalyticsDbContext>(
            field?.GetValue(repository));
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } =
            Environments.Development;

        public string ApplicationName { get; set; } =
            nameof(PersistenceDependencyInjectionTests);

        public string ContentRootPath { get; set; } =
            AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }
}
