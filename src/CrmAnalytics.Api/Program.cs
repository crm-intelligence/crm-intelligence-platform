using CrmAnalytics.Application.Abstractions.Integrations;
using CrmAnalytics.Application.Abstractions.Persistence;
using CrmAnalytics.Application.Authorization;
using CrmAnalytics.Application.Conversations;
using CrmAnalytics.Application.Identity;
using CrmAnalytics.Application.Notifications;
using CrmAnalytics.Application.ReportProcessing;
using CrmAnalytics.Application.ReportRequests;
using CrmAnalytics.Api.Authentication;
using CrmAnalytics.Api.ExceptionHandling;
using CrmAnalytics.Infrastructure.Integrations;
using CrmAnalytics.Infrastructure.Authorization;
using CrmAnalytics.Infrastructure.Identity;
using CrmAnalytics.Infrastructure.Persistence;
using CrmAnalytics.Infrastructure.Notifications;
using CrmAnalytics.Infrastructure.ReportProcessing;
using CrmAnalytics.Infrastructure.Messaging;
using CrmAnalytics.Infrastructure.QueryExecution;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;

var builder = WebApplication.CreateBuilder(args);

// Controller tabanlı HTTP API desteği.
builder.Services.AddControllers();

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// Authentication ve rapor authorization configuration'ı.
builder.Services
    .AddOptions<AzureAdOptions>()
    .Bind(builder.Configuration.GetSection(AzureAdOptions.SectionName));

builder.Services
    .AddOptions<CrmAnalyticsAuthenticationOptions>()
    .Bind(builder.Configuration.GetSection(
        CrmAnalyticsAuthenticationOptions.SectionName))
    .ValidateOnStart();

builder.Services.AddSingleton<
    IValidateOptions<CrmAnalyticsAuthenticationOptions>,
    CrmAnalyticsAuthenticationOptionsValidator>();

builder.Services
    .AddOptions<ReportAuthorizationOptions>()
    .Bind(builder.Configuration.GetSection(
        ReportAuthorizationOptions.SectionName))
    .ValidateOnStart();

builder.Services.AddSingleton<
    IValidateOptions<ReportAuthorizationOptions>,
    ReportAuthorizationOptionsValidator>();

var configuredAuthenticationMode = builder.Configuration[
    $"{CrmAnalyticsAuthenticationOptions.SectionName}:Mode"];

if (string.Equals(
        configuredAuthenticationMode,
        AuthenticationModes.Development,
        StringComparison.OrdinalIgnoreCase))
{
    builder.Services
        .AddAuthentication(
            DevelopmentAuthenticationDefaults.AuthenticationScheme)
        .AddScheme<
            AuthenticationSchemeOptions,
            DevelopmentAuthenticationHandler>(
            DevelopmentAuthenticationDefaults.AuthenticationScheme,
            _ => { });
}
else
{
    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddMicrosoftIdentityWebApi(
            builder.Configuration.GetSection(AzureAdOptions.SectionName));
}

builder.Services.AddSingleton<
    IAuthorizationHandler,
    ReportsAccessAuthorizationHandler>();

builder.Services.AddSingleton<
    IAuthorizationHandler,
    ReportPermissionAuthorizationHandler>();

builder.Services.AddSingleton<
    IReportRolePermissionPolicy,
    ConfigurationReportRolePermissionPolicy>();

builder.Services.AddSingleton<
    IReportPermissionEvaluator,
    ReportPermissionEvaluator>();

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(
        AuthenticationPolicies.ReportsAccess,
        policy =>
        {
            policy.RequireAuthenticatedUser();
            policy.AddRequirements(new ReportsAccessRequirement());
        });

    AddReportPermissionPolicy(
        options,
        AuthenticationPolicies.ReportsCreate,
        ReportPermission.Create);
    AddReportPermissionPolicy(
        options,
        AuthenticationPolicies.ReportsReadOwn,
        ReportPermission.ReadOwn);
    AddReportPermissionPolicy(
        options,
        AuthenticationPolicies.ReportsReviseOwn,
        ReportPermission.ReviseOwn);
    AddReportPermissionPolicy(
        options,
        AuthenticationPolicies.ReportsClarifyOwn,
        ReportPermission.ClarifyOwn);
    AddReportPermissionPolicy(
        options,
        AuthenticationPolicies.ReportsViewOwnHistory,
        ReportPermission.ViewOwnHistory);
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<
    ICurrentUserContextAccessor,
    HttpContextCurrentUserContextAccessor>();

// OpenAPI dokümanı oluşturma servisleri.
builder.Services.AddOpenApi();

// Rapor talebi application servisi.
builder.Services.AddScoped<
    IReportRequestService,
    ReportRequestService>();

builder.Services.AddSingleton<
    IReportRequestAccessService,
    ReportRequestAccessService>();

builder.Services.AddScoped<
    IReportProcessingService,
    ReportProcessingService>();

builder.Services.AddScoped<IReportRequestSubmissionService,
    ReportRequestSubmissionService>();

builder.Services.AddScoped<
    IConversationContextService,
    ConversationContextService>();

builder.Services
    .AddOptions<ReportProcessingQueueOptions>()
    .Bind(builder.Configuration.GetSection(
        ReportProcessingQueueOptions.SectionName))
    .ValidateOnStart();

builder.Services.AddSingleton<
    IValidateOptions<ReportProcessingQueueOptions>,
    ReportProcessingQueueOptionsValidator>();

builder.Services
    .AddOptions<TeamsNotificationOptions>()
    .Bind(builder.Configuration.GetSection(
        TeamsNotificationOptions.SectionName))
    .ValidateOnStart();

builder.Services.AddSingleton<
    IValidateOptions<TeamsNotificationOptions>,
    TeamsNotificationOptionsValidator>();

builder.Services.AddHttpClient(
    TeamsReportStatusNotificationClient.HttpClientName,
    (serviceProvider, httpClient) =>
    {
        var options = serviceProvider
            .GetRequiredService<IOptions<TeamsNotificationOptions>>()
            .Value;
        httpClient.Timeout = TimeSpan.FromSeconds(
            options.TimeoutSeconds);
    });

builder.Services.AddTransient<TeamsReportStatusNotificationClient>();
builder.Services.AddTransient<IReportStatusNotificationClient>(provider =>
    provider.GetRequiredService<TeamsReportStatusNotificationClient>());
builder.Services.AddTransient<IInternalTeamsNotificationClient>(provider =>
    provider.GetRequiredService<TeamsReportStatusNotificationClient>());

builder.Services.AddExternalResultIntegrations(
    builder.Configuration,
    builder.Environment);

builder.Services.AddCrmAnalyticsPersistence(
    builder.Configuration,
    builder.Environment);
builder.Services.AddQueryExecution(
    builder.Configuration,
    builder.Environment);
builder.Services.AddSqlProduction(
    builder.Configuration,
    builder.Environment);
builder.Services.AddReportDataAccess(
    builder.Configuration,
    builder.Environment);
builder.Services.AddCrmAnalyticsMessaging(
    builder.Configuration,
    builder.Environment);

// Uygulamanın çalışır durumda olduğunu kontrol eden temel health check.
builder.Services
    .AddHealthChecks()
    .AddCheck(
        name: "self",
        check: () => HealthCheckResult.Healthy(
            description: "CrmAnalytics API is running."),
        tags: new[] { "live" });

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment()) {
    app.MapOpenApi().AllowAnonymous();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseMiddleware<ReportRequestAuthorizationDiagnosticMiddleware>();
app.UseAuthorization();

app.MapControllers();

app.MapHealthChecks(
    pattern: "/health",
    options: new HealthCheckOptions {
        ResponseWriter = async (httpContext, healthReport) => {
            httpContext.Response.ContentType = "application/json";

            var response = new {
                status = healthReport.Status.ToString(),
                timestamp = DateTimeOffset.UtcNow,
                totalDurationMilliseconds =
                    healthReport.TotalDuration.TotalMilliseconds,
                checks = healthReport.Entries.Select(entry => new {
                    name = entry.Key,
                    status = entry.Value.Status.ToString(),
                    durationMilliseconds =
                        entry.Value.Duration.TotalMilliseconds
                })
            };

            await httpContext.Response.WriteAsJsonAsync(
                response,
                httpContext.RequestAborted);
        }
    })
    .AllowAnonymous();

app.MapHealthChecks(
    "/health/live",
    new HealthCheckOptions
    {
        Predicate = registration => registration.Tags.Contains("live"),
        ResponseWriter = WriteSafeHealthResponseAsync
    })
    .AllowAnonymous();

app.MapHealthChecks(
    "/health/ready",
    new HealthCheckOptions
    {
        Predicate = registration => registration.Tags.Contains("ready"),
        ResponseWriter = WriteSafeHealthResponseAsync
    })
    .AllowAnonymous();

app.Run();

static void AddReportPermissionPolicy(
    AuthorizationOptions options,
    string policyName,
    ReportPermission permission)
{
    options.AddPolicy(policyName, policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.AddRequirements(
            new ReportsAccessRequirement(),
            new ReportPermissionRequirement(permission));
    });
}

static Task WriteSafeHealthResponseAsync(
    HttpContext context,
    HealthReport report)
{
    context.Response.ContentType = "application/json";
    return context.Response.WriteAsJsonAsync(new
    {
        status = report.Status.ToString(),
        timestamp = DateTimeOffset.UtcNow,
        checks = report.Entries.Select(entry => new
        {
            name = entry.Key,
            status = entry.Value.Status.ToString()
        })
    }, context.RequestAborted);
}

public partial class Program;
