extern alias TeamsHost;

using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using BackendApiOptions =
    TeamsHost::CrmAnalytics.Teams.Configuration.BackendApiOptions;
using IReportRequestsApiClient =
    TeamsHost::CrmAnalytics.Teams.Backend.IReportRequestsApiClient;
using TeamsAuthenticationSettings =
    TeamsHost::CrmAnalytics.Teams.Configuration.TeamsAuthenticationSettings;
using ITeamsProactiveNotificationSender =
    TeamsHost::CrmAnalytics.Teams.Notifications.ITeamsProactiveNotificationSender;
using ITeamsReportCardActionHandler =
    TeamsHost::CrmAnalytics.Teams.Messaging.ITeamsReportCardActionHandler;
using TeamsHostMarker =
    TeamsHost::CrmAnalytics.Teams.TeamsHostMarker;

namespace CrmAnalytics.IntegrationTests;

public sealed class TeamsHostTests
{
    private static readonly string[] PublicPagePaths =
        ["/", "/privacy", "/terms"];

    [Fact]
    public void ServiceProvider_CanBeCreated()
    {
        using var factory = CreateFactory(Environments.Development, true);

        Assert.NotNull(factory.Services);
    }

    [Fact]
    public void ReportRequestsApiClient_CanBeResolved()
    {
        using var factory = CreateFactory(Environments.Development, true);

        var client = factory.Services
            .GetRequiredService<IReportRequestsApiClient>();

        Assert.NotNull(client);
    }

    [Fact]
    public void TeamsProactiveNotificationSender_CanBeResolved()
    {
        using var factory = CreateFactory(
            Environments.Development,
            true);

        var sender = factory.Services
            .GetRequiredService<ITeamsProactiveNotificationSender>();

        Assert.NotNull(sender);
    }

    [Fact]
    public void AdaptiveCardActionHandler_CanBeResolved()
    {
        using var factory = CreateFactory(
            Environments.Development,
            true);

        using var scope = factory.Services.CreateScope();
        var handler = scope.ServiceProvider
            .GetRequiredService<ITeamsReportCardActionHandler>();

        Assert.NotNull(handler);
    }

    [Fact]
    public void BackendApiOptions_AreValidatedAndAvailable()
    {
        using var factory = CreateFactory(Environments.Development, true);

        var options = factory.Services
            .GetRequiredService<IOptions<BackendApiOptions>>()
            .Value;

        Assert.Equal("https://localhost:7090", options.BaseUrl);
        Assert.Equal(15, options.TimeoutSeconds);
    }

    [Fact]
    public void Development_CanEnableSkipAuth()
    {
        using var factory = CreateFactory(Environments.Development, true);

        var settings = factory.Services
            .GetRequiredService<TeamsAuthenticationSettings>();

        Assert.True(settings.SkipAuthEnabled);
    }

    [Fact]
    public void Production_DevelopmentModeIsRejectedOnStart()
    {
        using var factory = new TeamsWebApplicationFactory(
            Environments.Production,
            skipAuth: false,
            authenticationMode: "Development",
            oauthConnectionName: string.Empty);

        var exception = Assert.Throws<OptionsValidationException>(
            () => _ = factory.Services);

        Assert.Contains(
            "Development",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Production_EntraModeWithSkipAuthIsRejectedOnStart()
    {
        using var factory = new TeamsWebApplicationFactory(
            Environments.Production,
            skipAuth: true,
            authenticationMode: "Entra",
            oauthConnectionName: "test-oauth");

        var exception = Assert.Throws<OptionsValidationException>(
            () => _ = factory.Services);

        Assert.Contains(
            "SkipAuth",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Production_ValidEntraConfigurationCanBuildSdkApp()
    {
        using var factory = new TeamsWebApplicationFactory(
            Environments.Production,
            skipAuth: false,
            authenticationMode: "Entra",
            oauthConnectionName: "test-oauth");

        Assert.NotNull(factory.Services);
    }

    [Fact]
    public async Task Health_ReturnsHealthyServiceResponse()
    {
        using var factory = CreateFactory(Environments.Development, true);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");
        var body = await response.Content
            .ReadFromJsonAsync<HealthResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(body);
        Assert.Equal("Healthy", body.Status);
        Assert.Equal("CrmAnalytics.Teams", body.Service);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/privacy")]
    [InlineData("/terms")]
    public async Task PublicPage_ReturnsHtml(string path)
    {
        using var factory = CreateFactory(Environments.Development, true);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("utf-8", response.Content.Headers.ContentType?.CharSet);
        Assert.False(string.IsNullOrWhiteSpace(body));
    }

    [Fact]
    public async Task PublicPages_DoNotContainCredentialShapedValues()
    {
        using var factory = CreateFactory(Environments.Development, true);
        using var client = factory.CreateClient();

        foreach (var path in PublicPagePaths)
        {
            var body = await client.GetStringAsync(path);

            Assert.DoesNotMatch(
                new Regex(@"(?i)(server|password|secret|token)\s*=\s*[^;\s<]+"),
                body);
            Assert.DoesNotMatch(
                new Regex(@"eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+"),
                body);
        }
    }

    [Fact]
    public void TeamsMessageRoute_RemainsMappedAndProtected()
    {
        using var factory = new TeamsWebApplicationFactory(
            Environments.Production,
            skipAuth: false,
            authenticationMode: "Entra",
            oauthConnectionName: "test-oauth");
        _ = factory.CreateClient();

        var messageEndpoint = factory.Services
            .GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Single(endpoint => string.Equals(
                endpoint.RoutePattern.RawText,
                "/api/messages",
                StringComparison.Ordinal));

        Assert.NotEmpty(
            messageEndpoint.Metadata.GetOrderedMetadata<IAuthorizeData>());
    }

    [Fact]
    public async Task HealthRoutes_RemainMapped()
    {
        using var factory = CreateFactory(Environments.Development, true);
        using var client = factory.CreateClient();

        var health = await client.GetAsync("/health");
        var live = await client.GetAsync("/health/live");
        var ready = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.NotEqual(HttpStatusCode.NotFound, ready.StatusCode);
    }

    private static TeamsWebApplicationFactory CreateFactory(
        string environmentName,
        bool skipAuth)
    {
        return new TeamsWebApplicationFactory(
            environmentName,
            skipAuth);
    }

    private sealed record HealthResponse(
        string Status,
        string Service);

    private sealed class TeamsWebApplicationFactory
        : WebApplicationFactory<TeamsHostMarker>
    {
        private readonly string _environmentName;
        private readonly bool _skipAuth;
        private readonly string _authenticationMode;
        private readonly string _oauthConnectionName;

        public TeamsWebApplicationFactory(
            string environmentName,
            bool skipAuth,
            string? authenticationMode = null,
            string? oauthConnectionName = null)
        {
            _environmentName = environmentName;
            _skipAuth = skipAuth;
            _authenticationMode = authenticationMode
                ?? (string.Equals(
                    environmentName,
                    Environments.Development,
                    StringComparison.Ordinal)
                        ? "Development"
                        : "Entra");
            _oauthConnectionName = oauthConnectionName
                ?? (_authenticationMode == "Entra"
                    ? "test-oauth"
                    : string.Empty);
        }

        protected override void ConfigureWebHost(
            IWebHostBuilder builder)
        {
            builder.UseEnvironment(_environmentName);
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["BackendApi:BaseUrl"] =
                            "https://localhost:7090",
                        ["BackendApi:TimeoutSeconds"] = "15",
                        ["ReportNotifications:Enabled"] = "false",
                        ["Teams:SkipAuth"] = _skipAuth.ToString(),
                        ["Teams:ClientId"] =
                            "11111111-1111-4111-8111-111111111111",
                        ["Teams:ClientSecret"] = "test-only-secret",
                        ["Teams:TenantId"] =
                            "22222222-2222-4222-8222-222222222222",
                        ["TeamsUserAuthentication:Mode"] =
                            _authenticationMode,
                        ["TeamsUserAuthentication:"
                            + "OAuthConnectionName"] =
                            _oauthConnectionName
                    });
            });
        }
    }
}
