using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace CrmAnalytics.IntegrationTests;

public sealed class IntegrationTestWebApplicationFactory
    : WebApplicationFactory<Program>
{
    public const string UserId =
        "11111111-1111-4111-8111-111111111111";

    public const string TenantId =
        "22222222-2222-4222-8222-222222222222";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Persistence:Provider"] = "InMemory",
                    ["Messaging:Provider"] = "InMemory",
                    ["QueryExecution:Provider"] = "Mock",
                    ["SqlProduction:Provider"] = "Mock",
                    ["TeamsNotifications:Enabled"] = "false",
                    ["ReportDataAccess:Provider"] = "Configuration",
                    ["ReportProcessing:Queue:Enabled"] = "false",
                    ["OutboxDispatcher:Enabled"] = "true",
                    ["CrmAnalyticsAuthentication:Mode"] = "Development",
                    ["CrmAnalyticsAuthentication:RequiredScope"] =
                        "access_as_user",
                    ["CrmAnalyticsAuthentication:Development:UserId"] =
                        UserId,
                    ["CrmAnalyticsAuthentication:Development:TenantId"] =
                        TenantId,
                    ["CrmAnalyticsAuthentication:Development:Roles:0"] =
                        "Report.User"
                });
        });
    }
}
