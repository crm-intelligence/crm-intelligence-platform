using CrmAnalytics.Application.Abstractions.Integrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CrmAnalytics.Infrastructure.Integrations;

public static class ExternalIntegrationsServiceCollectionExtensions
{
    public static IServiceCollection AddExternalResultIntegrations(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddOptions<MockExternalServicesOptions>()
            .Bind(configuration.GetSection(MockExternalServicesOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<MockExternalServicesOptions>,
            MockExternalServicesOptionsValidator>();
        services.AddOptions<AnalyticsOptions>()
            .Bind(configuration.GetSection(AnalyticsOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<AnalyticsOptions>,
            AnalyticsOptionsValidator>();
        services.AddOptions<ReportingOptions>()
            .Bind(configuration.GetSection(ReportingOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<ReportingOptions>,
            ReportingOptionsValidator>();

        services.AddSingleton<IQueryPlanningClient, MockQueryPlanningClient>();

        var analyticsProvider = configuration[
            $"{AnalyticsOptions.SectionName}:Provider"];
        if (AnalyticsOptionsValidator.Is(
                analyticsProvider, AnalyticsProviders.Direct))
            services.AddSingleton<IAnalyticsClient, DirectAnalyticsClient>();
        else if (AnalyticsOptionsValidator.Is(
                analyticsProvider, AnalyticsProviders.FabricJob))
        {
            services.AddHttpClient(FabricJobClient.HttpClientName, client =>
                client.Timeout = Timeout.InfiniteTimeSpan);
            services.AddSingleton<IFabricAccessTokenProvider,
                FabricAccessTokenProvider>();
            services.AddSingleton<IFabricJobClient, FabricJobClient>();
            services.AddSingleton<IAnalyticsClient, FabricJobAnalyticsClient>();
        }
        else
            services.AddSingleton<IAnalyticsClient, MockAnalyticsClient>();

        var reportingProvider = configuration[
            $"{ReportingOptions.SectionName}:Provider"];
        if (AnalyticsOptionsValidator.Is(
                reportingProvider, ReportingProviders.PowerBi))
        {
            services.AddHttpClient(PowerBiReportApiClient.HttpClientName,
                client => client.Timeout = TimeSpan.FromSeconds(30));
            services.AddSingleton<IPowerBiAccessTokenProvider,
                PowerBiAccessTokenProvider>();
            services.AddSingleton<IPowerBiReportApiClient,
                PowerBiReportApiClient>();
            services.AddSingleton<IReportClient, PowerBiReportClient>();
            services.AddSingleton<IPowerBiReportRouter,
                PowerBiReportRouter>();
        }
        else
            services.AddSingleton<IReportClient, MockReportClient>();

        services.AddHealthChecks().AddCheck<
            ExternalProviderReadinessHealthCheck>(
            "external-provider-configuration",
            tags: ["ready"]);

        return services;
    }
}
