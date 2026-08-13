using CrmAnalytics.Teams.Backend;
using CrmAnalytics.Teams.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CrmAnalytics.UnitTests;

public sealed class TeamsHostBuilderExtensionsTests
{
    [Fact]
    public async Task BackendHttpClient_UsesConfiguredBaseAddressAndTimeout()
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions
            {
                EnvironmentName = Environments.Development,
                ApplicationName = typeof(TeamsHostBuilderExtensions)
                    .Assembly.FullName
            });
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["BackendApi:BaseUrl"] = "https://backend.example",
                ["BackendApi:TimeoutSeconds"] = "23",
                ["Teams:SkipAuth"] = "false"
            });
        builder.AddCrmAnalyticsTeamsHost();

        await using var app = builder.Build();
        var factory = app.Services
            .GetRequiredService<IHttpClientFactory>();
        using var client = factory.CreateClient(
            ReportRequestsApiClient.HttpClientName);

        Assert.Equal(
            new Uri("https://backend.example"),
            client.BaseAddress);
        Assert.Equal(TimeSpan.FromSeconds(23), client.Timeout);
    }
}
