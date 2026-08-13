using CrmAnalytics.Application.Abstractions.Persistence;
using CrmAnalytics.Infrastructure.Persistence.SqlServer;
using CrmAnalytics.Application.Auditing;
using CrmAnalytics.Infrastructure.Auditing;
using CrmAnalytics.Application.Outbox;
using CrmAnalytics.Infrastructure.Outbox;
using CrmAnalytics.Application.Teams;
using CrmAnalytics.Infrastructure.Teams;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CrmAnalytics.Infrastructure.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddCrmAnalyticsPersistence(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        services
            .AddOptions<PersistenceOptions>()
            .Bind(configuration.GetSection(
                PersistenceOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<
            IValidateOptions<PersistenceOptions>,
            PersistenceOptionsValidator>();

        var provider = configuration[
            $"{PersistenceOptions.SectionName}:Provider"];

        if (PersistenceOptionsValidator.IsInMemory(provider))
        {
            services.AddSingleton<
                IReportRequestRepository,
                InMemoryReportRequestRepository>();
            services.AddSingleton<
                IConversationRepository,
                InMemoryConversationRepository>();
            services.AddScoped<
                IApplicationTransactionRunner,
                InMemoryApplicationTransactionRunner>();
            services.AddScoped<InMemoryApplicationAuditWriter>();
            services.AddScoped<IApplicationAuditWriter>(provider =>
                provider.GetRequiredService<InMemoryApplicationAuditWriter>());
            services.AddSingleton<InMemoryOutboxStore>();
            services.AddSingleton<InMemoryOutboxWriter>();
            services.AddSingleton<IOutboxWriter>(provider =>
                provider.GetRequiredService<InMemoryOutboxWriter>());
            services.AddSingleton<IOutboxStore>(provider =>
                provider.GetRequiredService<InMemoryOutboxStore>());
            services.AddSingleton<ITeamsNotificationTargetStore,
                InMemoryTeamsNotificationTargetStore>();
            services.AddSingleton<ITeamsNotificationDeliveryStore,
                InMemoryTeamsNotificationDeliveryStore>();
            services.AddSingleton<ITeamsCardActionSubmissionStore,
                InMemoryTeamsCardActionSubmissionStore>();

            return services;
        }

        var sqlServerOptions = configuration
            .GetSection(
                $"{PersistenceOptions.SectionName}:SqlServer")
            .Get<SqlServerPersistenceOptions>()
            ?? new SqlServerPersistenceOptions();
        var connectionString = configuration.GetConnectionString(
                SqlServerPersistenceOptions.ConnectionStringName)
            ?? string.Empty;

        services.AddDbContext<CrmAnalyticsDbContext>(options =>
        {
            options.UseSqlServer(
                connectionString,
                sqlServer =>
                {
                    sqlServer.CommandTimeout(
                        sqlServerOptions.CommandTimeoutSeconds);

                    if (sqlServerOptions.EnableRetryOnFailure)
                    {
                        sqlServer.EnableRetryOnFailure(
                            sqlServerOptions.MaxRetryCount,
                            TimeSpan.FromSeconds(
                                sqlServerOptions
                                    .MaxRetryDelaySeconds),
                            errorNumbersToAdd: null);
                    }
                });
        });

        services.AddScoped<SqlReportRequestRepository>();
        services.AddScoped<IReportRequestRepository>(
            serviceProvider => serviceProvider
                .GetRequiredService<SqlReportRequestRepository>());
        services.AddScoped<SqlConversationRepository>();
        services.AddScoped<IConversationRepository>(
            serviceProvider => serviceProvider
                .GetRequiredService<SqlConversationRepository>());
        services.AddScoped<
            IApplicationTransactionRunner,
            EfCoreApplicationTransactionRunner>();
        services.AddScoped<SqlApplicationAuditWriter>();
        services.AddScoped<IApplicationAuditWriter>(provider =>
            provider.GetRequiredService<SqlApplicationAuditWriter>());
        services.AddScoped<IOutboxWriter, SqlOutboxWriter>();
        services.AddScoped<IOutboxStore, SqlOutboxStore>();
        services.AddScoped<ITeamsNotificationTargetStore,
            SqlTeamsNotificationTargetStore>();
        services.AddScoped<ITeamsNotificationDeliveryStore,
            SqlTeamsNotificationDeliveryStore>();
        services.AddScoped<ITeamsCardActionSubmissionStore,
            SqlTeamsCardActionSubmissionStore>();

        services
            .AddHealthChecks()
            .AddCheck<CrmDatabaseHealthCheck>(
                name: "crm-database",
                tags: new[] { "ready" });
        services.AddHealthChecks().AddCheck<OutboxHealthCheck>(
            name: "transactional-outbox",
            tags: new[] { "ready" });

        return services;
    }
}
