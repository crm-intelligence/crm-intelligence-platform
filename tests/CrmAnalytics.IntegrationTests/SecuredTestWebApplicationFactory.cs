using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CrmAnalytics.IntegrationTests;

internal sealed class SecuredTestWebApplicationFactory
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["CrmAnalyticsAuthentication:Mode"] = "Entra",
                    ["Messaging:Provider"] = "InMemory",
                    ["OutboxDispatcher:Enabled"] = "true",
                    ["CrmAnalyticsAuthentication:RequiredScope"] =
                        "access_as_user",
                    ["AzureAd:Instance"] =
                        "https://login.microsoftonline.com/",
                    ["AzureAd:TenantId"] =
                        "22222222-2222-4222-8222-222222222222",
                    ["AzureAd:ClientId"] =
                        "33333333-3333-4333-8333-333333333333",
                    ["AzureAd:Audience"] =
                        "api://33333333-3333-4333-8333-333333333333",
                    ["ReportProcessing:Queue:Enabled"] = "false",
                    ["ReportDataAccess:Assignments:1:TenantId"] =
                        "22222222-2222-4222-8222-222222222222",
                    ["ReportDataAccess:Assignments:1:UserId"] =
                        "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
                    ["ReportDataAccess:Assignments:1:AllowAllRegions"] =
                        "true",
                    ["ReportDataAccess:Assignments:1:AllowAllStores"] =
                        "true",
                    ["ReportDataAccess:Assignments:2:TenantId"] =
                        "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb",
                    ["ReportDataAccess:Assignments:2:UserId"] =
                        "11111111-1111-4111-8111-111111111111",
                    ["ReportDataAccess:Assignments:2:AllowAllRegions"] =
                        "true",
                    ["ReportDataAccess:Assignments:2:AllowAllStores"] =
                        "true"
                });
        });
        builder.ConfigureTestServices(services =>
        {
            services
                .AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme =
                        TestAuthenticationDefaults.Scheme;
                    options.DefaultChallengeScheme =
                        TestAuthenticationDefaults.Scheme;
                    options.DefaultForbidScheme =
                        TestAuthenticationDefaults.Scheme;
                })
                .AddScheme<
                    AuthenticationSchemeOptions,
                    TestAuthenticationHandler>(
                    TestAuthenticationDefaults.Scheme,
                    _ => { });
        });
    }
}
