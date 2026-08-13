using CrmAnalytics.Application.Abstractions.Integrations;
using CrmAnalytics.Application.SqlProduction;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CrmAnalytics.Infrastructure.QueryExecution;

public static class QueryExecutionServiceCollectionExtensions
{
    public static IServiceCollection AddQueryExecution(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddOptions<QueryExecutionOptions>()
            .Bind(configuration.GetSection(
                QueryExecutionOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<
            IValidateOptions<QueryExecutionOptions>,
            QueryExecutionOptionsValidator>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IQueryResultReferenceFactory,
            QueryResultReferenceFactory>();
        services.AddSingleton<QueryResultMaterializer>();

        var provider = configuration[
            $"{QueryExecutionOptions.SectionName}:Provider"];
        if (string.Equals(
                provider,
                QueryExecutionProviders.Mock,
                StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IQueryExecutionClient,
                MockQueryExecutionClient>();
            return services;
        }

        services.AddSingleton<ISqlQueryConnectionFactory,
            SqlQueryConnectionFactory>();
        services.AddSingleton<IQueryExecutionParameterBinder,
            SqlQueryExecutionParameterBinder>();
        services.AddSingleton<IQueryExecutionClient,
            SqlClientQueryExecutionClient>();

        var section = configuration.GetSection(
            QueryExecutionOptions.SectionName);
        if (section.GetValue<bool>("Dwh:Enabled"))
        {
            services.AddHealthChecks().Add(new HealthCheckRegistration(
                "query-dwh",
                serviceProvider => new SqlQuerySourceHealthCheck(
                    serviceProvider.GetRequiredService<
                        ISqlQueryConnectionFactory>(),
                    SqlDataSource.Dwh),
                HealthStatus.Unhealthy,
                ["ready"]));
        }

        if (section.GetValue<bool>("Oltp:Enabled"))
        {
            services.AddHealthChecks().Add(new HealthCheckRegistration(
                "query-oltp",
                serviceProvider => new SqlQuerySourceHealthCheck(
                    serviceProvider.GetRequiredService<
                        ISqlQueryConnectionFactory>(),
                    SqlDataSource.Oltp),
                HealthStatus.Unhealthy,
                ["ready"]));
        }

        return services;
    }
}
