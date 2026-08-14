using Crm.Analytics.Sql.Audit;
using Crm.Analytics.Sql.Catalog;
using Crm.Analytics.Sql.Service;
using CrmAnalytics.Application.Abstractions.Integrations;
using CrmAnalytics.Application.ReportRequests;
using CrmAnalytics.Application.SqlAgent;
using CrmAnalytics.Application.SqlProduction;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CrmAnalytics.Infrastructure.Integrations;

public static class SqlProductionServiceCollectionExtensions
{
    public static IServiceCollection AddSqlProduction(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        services.AddOptions<SqlProductionProviderOptions>()
            .Bind(configuration.GetSection(
                SqlProductionProviderOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<
            IValidateOptions<SqlProductionProviderOptions>,
            SqlProductionProviderOptionsValidator>();
        services.AddOptions<OllamaOptions>()
            .Bind(configuration.GetSection(OllamaOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<OllamaOptions>,
            OllamaOptionsValidator>();
        services.AddOptions<SemanticEmbeddingOptions>()
            .Bind(configuration.GetSection(SemanticEmbeddingOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<SemanticEmbeddingOptions>,
            SemanticEmbeddingOptionsValidator>();

        services.AddSingleton<SqlProductionScopeCompatibilityMapper>();
        services.AddSingleton(_ => SemanticCatalogRegistry.CreateDefault());
        services.AddSingleton<IDecisionAuditWriter,
            SafeSqlDecisionAuditWriter>();
        services.AddSingleton<ISqlAgentBackend>(serviceProvider =>
        {
            var configured = serviceProvider.GetRequiredService<
                IOptions<SqlProductionProviderOptions>>().Value;
            return SqlProductionFactory.CreateSqlAgentBackendForOlist(
                serviceProvider.GetRequiredService<IDecisionAuditWriter>(),
                new Crm.Analytics.Sql.Service.SqlProductionOptions(
                    configured.ConfidenceThreshold,
                    configured.SqlVersionName),
                serviceProvider.GetRequiredService<SemanticCatalogRegistry>());
        });
        services.AddScoped<SqlAgentService>();
        services.AddScoped<ISqlAgentService>(serviceProvider =>
            serviceProvider.GetRequiredService<SqlAgentService>());
        services.AddScoped<IPreparedSqlAgentCapabilityService>(
            serviceProvider =>
                serviceProvider.GetRequiredService<SqlAgentService>());
        services.AddScoped<IRoutedReportRequestSubmissionService,
            CapabilityAwareReportRequestSubmissionService>();

        var provider = configuration[
            $"{SqlProductionProviderOptions.SectionName}:Provider"];
        var ollamaEnabled = configuration.GetValue<bool>(
            $"{OllamaOptions.SectionName}:Enabled");
        if ((SqlProductionProviderOptionsValidator.IsMock(provider)
                || string.IsNullOrWhiteSpace(provider))
            && !ollamaEnabled)
        {
            // The existing deterministic mock pipeline remains the Development/test
            // implementation and is selected when ISqlProductionClient is absent.
            return services;
        }

        services.AddSingleton<OllamaCanonicalContract>();
        services.AddSingleton<SemanticDocumentFactory>();
        services.AddHttpClient<ISemanticEmbeddingClient,
            OllamaSemanticEmbeddingClient>((serviceProvider, httpClient) =>
        {
            var configured = serviceProvider.GetRequiredService<
                IOptions<SemanticEmbeddingOptions>>().Value;
            httpClient.BaseAddress = new Uri(configured.BaseUrl, UriKind.Absolute);
            httpClient.Timeout = TimeSpan.FromSeconds(configured.TimeoutSeconds);
        });
        services.AddSingleton<ISemanticEmbeddingIndex, SemanticEmbeddingIndex>();
        services.AddSingleton<IFilterLiteralResolver, FilterLiteralResolver>();
        services.AddSingleton<ISemanticCandidateDiscriminator,
            DeterministicSemanticCandidateDiscriminator>();
        services.AddSingleton<ISemanticSourceResolver,
            CatalogSemanticSourceResolver>();
        services.AddSingleton<ISemanticEmbeddingResolver, SemanticEmbeddingResolver>();
        services.AddSingleton<ISemanticSlotIntentDetector,
            SemanticSlotIntentDetector>();
        services.AddSingleton<ISemanticCompletenessGate,
            SemanticCompletenessGate>();
        services.AddSingleton<ISemanticCanonicalRequestAssembler,
            SemanticCanonicalRequestAssembler>();
        services.AddSingleton<ILlmFirstCanonicalRequestAssembler,
            LlmFirstCanonicalRequestAssembler>();
        services.AddHttpClient<IOllamaStructuredPlanningClient,
            OllamaStructuredPlanningClient>((serviceProvider, httpClient) =>
        {
            var configured = serviceProvider
                .GetRequiredService<IOptions<OllamaOptions>>().Value;
            httpClient.BaseAddress = new Uri(configured.BaseUrl,
                UriKind.Absolute);
            httpClient.Timeout = TimeSpan.FromSeconds(
                configured.TimeoutSeconds);
        });
        services.AddSingleton<ISqlProductionService>(serviceProvider =>
        {
            var configured = serviceProvider.GetRequiredService<
                IOptions<SqlProductionProviderOptions>>().Value;
            return SqlProductionFactory.CreateForOlist(
                serviceProvider.GetRequiredService<IDecisionAuditWriter>(),
                options: new Crm.Analytics.Sql.Service.SqlProductionOptions(
                    configured.ConfidenceThreshold,
                    configured.SqlVersionName),
                semanticCatalogs: serviceProvider
                    .GetRequiredService<SemanticCatalogRegistry>());
        });
        services.AddSingleton<ISqlProductionClient,
            CrmAnalyticsSqlProductionClient>();
        return services;
    }
}
