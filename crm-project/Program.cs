using Microsoft.AspNetCore.Mvc;
using crm_project.Models;
using crm_project.Services;
using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Identity.Web;
using Microsoft.OpenApi.Models;
using Crm.Analytics.Sql.Audit;
using Crm.Analytics.Sql.Service;

// crm_project.Models.CanonicalRequest ile Crm.Analytics.Sql.Contracts.CanonicalRequest
// ayni ada sahip iki farkli tip. Kutuphanenin tipi burada dogrudan kullanilmiyor
// (servis kendi kanonik formunu prompt'tan uretiyor), bu yuzden Contracts namespace'ini
// acmak yerine ihtiyac duyulan tip tek tek alias'landi: boylece CS0104 belirsizligi
// olusmuyor ve hangi CanonicalRequest'ten bahsedildigi okuyan icin net kaliyor.
using GuardrailDecision = Crm.Analytics.Sql.Contracts.GuardrailDecision;

var builder = WebApplication.CreateBuilder(args);

// Application Insights telemetrisi.
// Connection string varsa aktif olur; test ve CI ortamında yoksa hata vermez.
var applicationInsightsConnectionString =
    builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]
    ?? builder.Configuration["ApplicationInsights:ConnectionString"];

if (!string.IsNullOrWhiteSpace(applicationInsightsConnectionString))
{
    builder.Services.AddApplicationInsightsTelemetry(options =>
    {
        options.ConnectionString = applicationInsightsConnectionString;
    });
}

// Microsoft Entra ID JWT token doğrulaması
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(
        builder.Configuration.GetSection("AzureAd"));

builder.Services.AddAuthorization();

// Güvenlik ve audit log servisleri
builder.Services.AddSingleton<SecurityPolicyService>();
builder.Services.AddSingleton<AuditLogService>();

// ---------------------------------------------------------------------------
// SQL üretim katmanı (Crm.Analytics.Sql) — ENTEGRASYON.md §2
// ---------------------------------------------------------------------------
// Kütüphane bilinçli olarak IServiceCollection uzantısı sunmuyor: bu, kütüphaneyi
// belirli bir DI kapsayıcısına bağlardı. Fabrika düz bir nesne döndürüyor.
//
// Servis durumsuz ve thread-safe olduğu için singleton. Katalog ve allow-list
// doğrulaması kurulum anında yapılıyor: hatalı bir katalog satırı ilk istekte
// değil UYGULAMA AÇILIŞINDA hata verir.
builder.Services.AddSingleton<ClaimsDataScopeResolver>();
builder.Services.AddSingleton<IDecisionAuditWriter, SqlDecisionAuditWriter>();

builder.Services.AddSingleton<ISqlProductionService>(provider =>
    SqlProductionFactory.CreateForOlist(
        provider.GetRequiredService<IDecisionAuditWriter>()));

// Swagger
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "crm-project",
        Version = "v1"
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Microsoft Entra ID access token girin."
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Request ID korelasyon middleware'i
app.Use(async (context, next) =>
{
    const string headerName = "X-Request-ID";

    var requestId = context.Request.Headers[headerName].FirstOrDefault();

    if (string.IsNullOrWhiteSpace(requestId))
    {
        requestId = Guid.NewGuid().ToString();
    }

    context.Items["RequestId"] = requestId;
    context.Response.Headers[headerName] = requestId;

    var traceId = Activity.Current?.TraceId.ToString()
                  ?? context.TraceIdentifier;

    using (app.Logger.BeginScope(new Dictionary<string, object>
    {
        ["RequestId"] = requestId,
        ["TraceId"] = traceId
    }))
    {
        app.Logger.LogInformation(
            "İstek başladı. RequestId: {RequestId}, TraceId: {TraceId}, Method: {Method}, Path: {Path}",
            requestId,
            traceId,
            context.Request.Method,
            context.Request.Path);

        await next();

        app.Logger.LogInformation(
            "İstek tamamlandı. RequestId: {RequestId}, TraceId: {TraceId}, StatusCode: {StatusCode}",
            requestId,
            traceId,
            context.Response.StatusCode);
    }
});

// Kimlik doğrulama ve yetkilendirme
app.UseAuthentication();
app.UseAuthorization();

// Token gerektiren API endpoint'i
app.MapPost(
    "/api/requests",
    (
        CanonicalRequest request,
        [FromHeader(Name = "X-Request-ID")] string? requestIdHeader,
        HttpContext context,
        ILogger<Program> logger,
        SecurityPolicyService securityPolicyService,
        AuditLogService auditLogService
    ) =>
    {
        var requestId = context.Items["RequestId"]?.ToString()
                        ?? requestIdHeader
                        ?? request.RequestId
                        ?? Guid.NewGuid().ToString();

        var authenticatedUser =
            context.User.Identity?.Name
            ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? request.UserId;

        var userRole =
            context.User.FindFirst(ClaimTypes.Role)?.Value
            ?? context.User.FindFirst("roles")?.Value;

        var userRegion =
            context.User.FindFirst("region")?.Value;

        var isAdmin = string.Equals(
            userRole,
            "Admin",
            StringComparison.OrdinalIgnoreCase);

        var isRegionManager = string.Equals(
            userRole,
            "RegionManager",
            StringComparison.OrdinalIgnoreCase);

        logger.LogInformation(
            "Canonical talep alındı. RequestId: {RequestId}, UseCase: {UseCase}, Role: {Role}",
            requestId,
            request.UseCase,
            userRole);

        // RLS kontrolü:
        // Admin tüm bölgelere erişebilir.
        // RegionManager yalnızca kendi bölgesine erişebilir.
        if (!isAdmin &&
            isRegionManager &&
            !string.Equals(
                userRegion,
                request.Region,
                StringComparison.OrdinalIgnoreCase))
        {
            var deniedAuditEntry = new AuditLogEntry
            {
                TimestampUtc = DateTime.UtcNow,
                RequestId = requestId,
                UserId = string.Empty,
                Decision = "DENY",
                Reason = "Kullanıcı kendi bölgesi dışındaki veriye erişemez.",
                Source = request.Source,
                Query = string.Empty,
                TargetTable = string.Empty,
                Region = string.Empty
            };

            auditLogService.Write(deniedAuditEntry);

            logger.LogWarning(
                "RLS erişimi reddedildi. RequestId: {RequestId}",
                requestId);

            return Results.Json(
                new
                {
                    message = "Bölge bazlı erişim reddedildi.",
                    requestId,
                    decision = "DENY",
                    reason = "Kullanıcı kendi bölgesi dışındaki veriye erişemez.",
                    userRegion,
                    requestedRegion = request.Region
                },
                statusCode: StatusCodes.Status403Forbidden);
        }

        var securityDecision = securityPolicyService.Evaluate(request);

        var auditEntry = new AuditLogEntry
        {
            TimestampUtc = DateTime.UtcNow,
            RequestId = requestId,
            UserId = string.Empty,
            Decision = securityDecision.Decision,
            Reason = securityDecision.Reason,
            Source = request.Source,
            Query = string.Empty,
            TargetTable = string.Empty,
            Region = string.Empty
        };

        auditLogService.Write(auditEntry);

        if (!securityDecision.IsAllowed)
        {
            return Results.Json(
                new
                {
                    message = "Talep güvenlik politikası nedeniyle reddedildi.",
                    requestId,
                    decision = securityDecision.Decision,
                    reason = securityDecision.Reason
                },
                statusCode: StatusCodes.Status403Forbidden);
        }

        return Results.Ok(new
        {
            message = "Canonical request güvenlik kontrolünden geçti.",
            requestId,
            decision = securityDecision.Decision,
            user = new
            {
                userId = authenticatedUser,
                role = userRole,
                region = userRegion
            },
            request
        });
    })
    .WithName("CreateCanonicalRequest")
    .RequireAuthorization();

// ---------------------------------------------------------------------------
// POST /api/reports — doğal dil talebinden kontrollü SQL üretimi
// ---------------------------------------------------------------------------
// /api/requests ucundan ayrı tutuldu ve o uca DOKUNULMADI: orada istemci kendi
// Region'ını gövdede gönderiyor, burada kapsam yalnızca token'dan türetiliyor.
// İkisini tek uçta birleştirmek, istemciden gelen bölgenin yetki kararına
// sızmasına açık kapı bırakırdı.
//
// Bu uç sorguyu ÇALIŞTIRMAZ. Çalıştırma katmanı henüz yok; dönen şey
// guardrail'dan geçmiş, parametreli bir sorgu ve onun görsel önerisi.
app.MapPost(
    "/api/reports",
    (
        ReportRequest body,
        HttpContext context,
        ILogger<Program> logger,
        ClaimsDataScopeResolver scopeResolver,
        ISqlProductionService sqlProductionService
    ) =>
    {
        var requestId = context.Items["RequestId"]?.ToString()
                        ?? Guid.NewGuid().ToString();

        // Kimlik yalnızca token'dan. /api/requests'teki gibi gövdedeki UserId'ye
        // düşülmüyor: o düşüş, token'da ad claim'i yoksa denetim kaydındaki UserId'yi
        // istemci kontrolüne bırakıyor.
        var userId =
            context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? context.User.Identity?.Name;

        // Kapsam token'dan türetiliyor; çözümlenemezse guardrail GR007 ile reddeder.
        var scope = scopeResolver.Resolve(context.User);

        var response = sqlProductionService.Produce(new SqlProductionRequest
        {
            Prompt = body.Prompt,
            RequestId = requestId,
            ConversationId = body.ConversationId,
            Scope = scope,
            Today = DateOnly.FromDateTime(DateTime.UtcNow),
            UserId = userId
        });

        logger.LogInformation(
            "SQL üretim kararı. RequestId: {RequestId}, Karar: {Decision}, Gerekçe: {ReasonCode}, KapsamÇözümlendi: {ScopeResolved}",
            requestId,
            response.Decision,
            response.ReasonCode,
            scope.IsResolvable);

        var teamsResponse = TeamsReportResponse.From(response, body.ConversationId);

        // Karar → HTTP durumu. Reddedilen talep 403: yetki/güvenlik kararı.
        // Netleştirme 200: hata değil, kullanıcıdan bilgi isteniyor.
        return response.Decision switch
        {
            GuardrailDecision.Rejected =>
                Results.Json(teamsResponse, statusCode: StatusCodes.Status403Forbidden),
            _ => Results.Ok(teamsResponse)
        };
    })
    .WithName("CreateGuardedReport")
    .RequireAuthorization();

app.Run();

public partial class Program
{
}
