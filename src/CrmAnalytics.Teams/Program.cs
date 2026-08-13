using CrmAnalytics.Teams.Hosting;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Teams.Plugins.AspNetCore.Extensions;

var builder = WebApplication.CreateBuilder(args);
builder.AddCrmAnalyticsTeamsHost();

var app = builder.Build();

app.MapCrmAnalyticsPublicPages();

app.MapGet(
    "/health",
    () => Results.Ok(new
    {
        status = "Healthy",
        service = "CrmAnalytics.Teams"
    }));

app.MapGet("/health/live", () => Results.Ok(new { status = "Healthy" }));
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready"),
    ResponseWriter = (context, report) =>
    {
        context.Response.ContentType = "application/json";
        return context.Response.WriteAsJsonAsync(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString()
            })
        }, context.RequestAborted);
    }
});

app.MapCrmAnalyticsReportNotifications();

var teams = app.UseTeams();
teams.MapCrmAnalyticsSignInEvents(app.Services);
teams.MapCrmAnalyticsReportRequests(app.Services);
teams.MapCrmAnalyticsCardActions(app.Services);

app.Run();

public partial class Program;

namespace CrmAnalytics.Teams
{
    public sealed class TeamsHostMarker;
}
