using CrmAnalytics.Teams.Authentication;
using CrmAnalytics.Teams.Backend;
using CrmAnalytics.Teams.Configuration;
using CrmAnalytics.Teams.Messaging;
using CrmAnalytics.Teams.Notifications;
using CrmAnalytics.Teams.Planning;
using Microsoft.Extensions.Options;
using Microsoft.Teams.Apps;
using Microsoft.Teams.Plugins.AspNetCore.Extensions;

namespace CrmAnalytics.Teams.Hosting;

public static class TeamsHostBuilderExtensions
{
    public static WebApplicationBuilder AddCrmAnalyticsTeamsHost(
        this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services
            .AddOptions<BackendApiOptions>()
            .Bind(builder.Configuration.GetSection(
                BackendApiOptions.SectionName))
            .ValidateOnStart();

        builder.Services.AddSingleton<
            IValidateOptions<BackendApiOptions>,
            BackendApiOptionsValidator>();
        builder.Services.AddSingleton<IValidateOptions<BackendApiOptions>,
            DurableBackendApiOptionsValidator>();

        builder.Services
            .AddOptions<TeamsHostOptions>()
            .Bind(builder.Configuration.GetSection(
                TeamsHostOptions.SectionName))
            .ValidateOnStart();

        builder.Services.AddSingleton<
            IValidateOptions<TeamsHostOptions>,
            TeamsHostOptionsValidator>();

        builder.Services
            .AddOptions<TeamsUserAuthenticationOptions>()
            .Bind(builder.Configuration.GetSection(
                TeamsUserAuthenticationOptions.SectionName))
            .ValidateOnStart();

        builder.Services.AddSingleton<
            IValidateOptions<TeamsUserAuthenticationOptions>,
            TeamsUserAuthenticationOptionsValidator>();

        builder.Services
            .AddOptions<ReportNotificationEndpointOptions>()
            .Bind(builder.Configuration.GetSection(
                ReportNotificationEndpointOptions.SectionName))
            .ValidateOnStart();

        builder.Services.AddSingleton<
            IValidateOptions<ReportNotificationEndpointOptions>,
            ReportNotificationEndpointOptionsValidator>();

        builder.Services
            .AddOptions<CopilotStudioOptions>()
            .Bind(builder.Configuration.GetSection(
                CopilotStudioOptions.SectionName))
            .ValidateOnStart();

        builder.Services.AddSingleton<
            IValidateOptions<CopilotStudioOptions>,
            CopilotStudioOptionsValidator>();

        var configuredSkipAuth = builder.Configuration.GetValue<bool>(
            $"{TeamsHostOptions.SectionName}:SkipAuth");
        var skipAuthEnabled =
            builder.Environment.IsDevelopment() && configuredSkipAuth;

        builder.Services.AddSingleton(
            new TeamsAuthenticationSettings(skipAuthEnabled));

        builder.Services
            .AddHttpClient(
                ReportRequestsApiClient.HttpClientName,
                (serviceProvider, httpClient) =>
                {
                    var options = serviceProvider
                        .GetRequiredService<IOptions<BackendApiOptions>>()
                        .Value;

                    httpClient.BaseAddress = new Uri(
                        options.BaseUrl,
                        UriKind.Absolute);
                    httpClient.Timeout = TimeSpan.FromSeconds(
                        options.TimeoutSeconds);
                    if (!string.IsNullOrWhiteSpace(options.InternalApiKey))
                    {
                        httpClient.DefaultRequestHeaders.TryAddWithoutValidation(
                            CrmAnalytics.Contracts.InternalTeams
                                .TeamsInternalHttpConstants.ApiKeyHeaderName,
                            options.InternalApiKey);
                    }
                });

        builder.Services.AddTransient<IReportRequestsApiClient>(
            serviceProvider =>
            {
                var httpClientFactory = serviceProvider
                    .GetRequiredService<IHttpClientFactory>();
                return new ReportRequestsApiClient(
                    httpClientFactory.CreateClient(
                        ReportRequestsApiClient.HttpClientName));
            });
        builder.Services
            .AddHttpClient<ICopilotStudioPlanningClient,
                DirectLineCopilotStudioPlanningClient>(
                DirectLineCopilotStudioPlanningClient.HttpClientName,
                httpClient =>
                {
                    httpClient.Timeout = Timeout.InfiniteTimeSpan;
                })
            .RemoveAllLoggers();
        builder.Services.AddHealthChecks().AddCheck<BackendApiHealthCheck>(
            "backend-api", tags: ["ready"]);

        builder.Services.AddScoped<TeamsReportRequestHandler>();
        builder.Services.AddSingleton<TeamsMessageDispatcher>();
        builder.Services.AddTransient<
            ITeamsBackendAuthorizationCoordinator,
            TeamsBackendAuthorizationCoordinator>();
        builder.Services.AddScoped<
            ITeamsReportCardActionHandler,
            TeamsReportCardActionHandler>();
        builder.Services.AddSingleton<ITeamsClarificationActionQueue,
            TeamsClarificationActionQueue>();
        builder.Services.AddScoped<TeamsClarificationActionProcessor>();
        builder.Services.AddHostedService<TeamsClarificationActionWorker>();
        builder.Services.AddSingleton<ITeamsRevisionActionQueue,
            TeamsRevisionActionQueue>();
        builder.Services.AddScoped<TeamsRevisionActionProcessor>();
        builder.Services.AddHostedService<TeamsRevisionActionWorker>();
        builder.Services.AddSingleton<InMemoryTeamsCardActionSubmissionStore>();
        builder.Services.AddSingleton<InMemoryTeamsNotificationTargetStore>();
        builder.Services.AddSingleton<InMemoryReportNotificationDeliveryStore>();
        builder.Services.AddSingleton<DurableBackendActionFallbackStore>();
        builder.Services.AddSingleton<DurableBackendNotificationTargetFallbackStore>();
        builder.Services.AddSingleton<DurableBackendDeliveryFallbackStore>();
        builder.Services.AddSingleton<ITeamsCardActionSubmissionStore,
            ConfigurableTeamsCardActionSubmissionStore>();
        builder.Services.AddSingleton<ITeamsNotificationTargetStore,
            ConfigurableTeamsNotificationTargetStore>();
        builder.Services.AddSingleton<IReportNotificationDeliveryStore,
            ConfigurableReportNotificationDeliveryStore>();
        builder.Services.AddSingleton<
            IReportNotificationCardFactory,
            ReportNotificationCardFactory>();
        builder.Services.AddTransient<
            ITeamsProactiveNotificationSender,
            TeamsProactiveNotificationSender>();

        var teamsUserAuthentication = builder.Configuration
            .GetSection(TeamsUserAuthenticationOptions.SectionName)
            .Get<TeamsUserAuthenticationOptions>()
            ?? new TeamsUserAuthenticationOptions();
        var teamsAppBuilder = App.Builder()
            .AddCrmAnalyticsTeamsUserOAuth(teamsUserAuthentication);

        builder.AddTeams(
            teamsAppBuilder,
            skipAuth: skipAuthEnabled);

        return builder;
    }
}
