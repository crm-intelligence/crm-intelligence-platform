using System.Security.Claims;
using CrmAnalytics.Application.Authorization;
using CrmAnalytics.Application.Exceptions;
using CrmAnalytics.Application.Identity;
using Microsoft.Extensions.Options;

namespace CrmAnalytics.Api.Authentication;

public sealed class ReportRequestAuthorizationDiagnosticMiddleware
{
    private const string TargetPath = "/api/report-requests";
    private const string NoForbiddenStage = "None";
    private readonly RequestDelegate _next;
    private readonly ILogger<ReportRequestAuthorizationDiagnosticMiddleware>
        _logger;
    private readonly CrmAnalyticsAuthenticationOptions _authenticationOptions;
    private readonly AzureAdOptions _azureAdOptions;

    public ReportRequestAuthorizationDiagnosticMiddleware(
        RequestDelegate next,
        ILogger<ReportRequestAuthorizationDiagnosticMiddleware> logger,
        IOptions<CrmAnalyticsAuthenticationOptions> authenticationOptions,
        IOptions<AzureAdOptions> azureAdOptions)
    {
        _next = next;
        _logger = logger;
        _authenticationOptions = authenticationOptions.Value;
        _azureAdOptions = azureAdOptions.Value;
    }

    public async Task InvokeAsync(
        HttpContext context,
        IReportRolePermissionPolicy rolePermissionPolicy,
        IReportPermissionEvaluator permissionEvaluator,
        IUserDataAccessAssignmentStore assignmentStore)
    {
        if (!IsTargetRequest(context.Request))
        {
            await _next(context);
            return;
        }

        var principal = context.User;
        var authenticationSucceeded =
            principal.Identity?.IsAuthenticated == true;

        // This middleware runs after UseAuthentication. A successful Entra
        // principal therefore means JWT signature, issuer, lifetime and audience
        // validation have already completed without reading or logging the token.
        var audienceValidated = authenticationSucceeded;
        var oid = FindClaim(
            principal,
            MicrosoftIdentityClaimTypes.ObjectId,
            MicrosoftIdentityClaimTypes.ObjectIdUri);
        var tid = FindClaim(
            principal,
            MicrosoftIdentityClaimTypes.TenantId,
            MicrosoftIdentityClaimTypes.TenantIdUri);
        var roles = ReadRoles(principal);
        var hasAccessAsUserScope = MicrosoftIdentityScopeResolver.HasScope(
            principal,
            _authenticationOptions.RequiredScope);
        var tokenTenantMatchesConfiguredTenant = IdentifiersEqual(
            tid,
            _azureAdOptions.TenantId);
        var hasReportUserRole = roles.Contains(
            ReportAppRoles.User,
            StringComparer.Ordinal);
        var reportUserRoleMappingResult = hasReportUserRole
            && rolePermissionPolicy.RoleHasPermission(
                ReportAppRoles.User,
                ReportPermission.Create);

        AuthenticatedUserContext? user = null;
        if (authenticationSucceeded && oid is not null && tid is not null)
        {
            try
            {
                user = new AuthenticatedUserContext(oid, tid, roles);
            }
            catch (ArgumentException)
            {
                // Invalid identifiers remain an Authentication diagnostic.
            }
        }

        var reportsCreatePermissionResult = user is not null
            && permissionEvaluator.HasPermission(
                user,
                ReportPermission.Create);
        var inspection = UserDataAccessAssignmentInspection.NotFound;
        var scopeLookupAttempted = false;

        if (authenticationSucceeded
            && audienceValidated
            && hasAccessAsUserScope
            && tokenTenantMatchesConfiguredTenant
            && reportsCreatePermissionResult
            && user is not null)
        {
            scopeLookupAttempted = true;
            inspection = await assignmentStore.InspectAsync(
                user.TenantId,
                user.UserId,
                context.RequestAborted);
        }

        var diagnostic = new DiagnosticState(
            authenticationSucceeded,
            audienceValidated,
            hasAccessAsUserScope,
            tokenTenantMatchesConfiguredTenant,
            Suffix(oid),
            Suffix(tid),
            SafeRoles(roles),
            reportUserRoleMappingResult,
            reportsCreatePermissionResult,
            Suffix(oid),
            scopeLookupAttempted && inspection.AssignmentFound,
            scopeLookupAttempted && inspection.IsActive,
            scopeLookupAttempted && inspection.AllowAllRegions,
            scopeLookupAttempted && inspection.AllowAllStores,
            scopeLookupAttempted && inspection.IntegrityValid,
            hasReportUserRole);

        try
        {
            await _next(context);
        }
        catch (DataAccessPolicyIntegrityException)
        {
            Log(diagnostic, "DataScopeIntegrity");
            throw;
        }
        catch (ForbiddenAccessException)
        {
            Log(diagnostic, ForbiddenStage(diagnostic));
            throw;
        }

        var forbiddenStage = context.Response.StatusCode
            == StatusCodes.Status403Forbidden
                ? ForbiddenStage(diagnostic)
                : NoForbiddenStage;
        Log(diagnostic, forbiddenStage);
    }

    private void Log(DiagnosticState diagnostic, string forbiddenStage)
    {
        _logger.LogInformation(
            "ReportRequestAuthorizationDiagnostic "
                + "AuthenticationSucceeded={AuthenticationSucceeded} "
                + "AudienceValidated={AudienceValidated} "
                + "HasAccessAsUserScope={HasAccessAsUserScope} "
                + "TokenTenantMatchesConfiguredTenant={TokenTenantMatchesConfiguredTenant} "
                + "TokenOidSuffix={TokenOidSuffix} "
                + "TokenTidSuffix={TokenTidSuffix} "
                + "TokenRoles={TokenRoles} "
                + "ReportUserRoleMappingResult={ReportUserRoleMappingResult} "
                + "ReportsCreatePermissionResult={ReportsCreatePermissionResult} "
                + "ScopeLookupOidSuffix={ScopeLookupOidSuffix} "
                + "ScopeAssignmentFound={ScopeAssignmentFound} "
                + "ScopeIsActive={ScopeIsActive} "
                + "AllowAllRegions={AllowAllRegions} "
                + "AllowAllStores={AllowAllStores} "
                + "ForbiddenStage={ForbiddenStage}",
            diagnostic.AuthenticationSucceeded,
            diagnostic.AudienceValidated,
            diagnostic.HasAccessAsUserScope,
            diagnostic.TokenTenantMatchesConfiguredTenant,
            diagnostic.TokenOidSuffix,
            diagnostic.TokenTidSuffix,
            diagnostic.TokenRoles,
            diagnostic.ReportUserRoleMappingResult,
            diagnostic.ReportsCreatePermissionResult,
            diagnostic.ScopeLookupOidSuffix,
            diagnostic.ScopeAssignmentFound,
            diagnostic.ScopeIsActive,
            diagnostic.AllowAllRegions,
            diagnostic.AllowAllStores,
            forbiddenStage);
    }

    private static string ForbiddenStage(DiagnosticState diagnostic)
    {
        if (!diagnostic.AuthenticationSucceeded
            || !diagnostic.AudienceValidated
            || !diagnostic.HasAccessAsUserScope
            || !diagnostic.TokenTenantMatchesConfiguredTenant)
        {
            return "Authentication";
        }

        if (!diagnostic.HasReportUserRole)
        {
            return "RoleMapping";
        }

        if (!diagnostic.ReportUserRoleMappingResult
            || !diagnostic.ReportsCreatePermissionResult)
        {
            return "Permission";
        }

        if (!diagnostic.ScopeIntegrityValid)
        {
            return "DataScopeIntegrity";
        }

        if (!diagnostic.ScopeAssignmentFound)
        {
            return "DataScopeNotFound";
        }

        if (!diagnostic.ScopeIsActive)
        {
            return "DataScopeInactive";
        }

        return "Permission";
    }

    private static bool IsTargetRequest(HttpRequest request) =>
        HttpMethods.IsPost(request.Method)
        && string.Equals(
            request.Path.Value?.TrimEnd('/'),
            TargetPath,
            StringComparison.OrdinalIgnoreCase);

    private static string? FindClaim(
        ClaimsPrincipal principal,
        params string[] claimTypes) =>
        principal.Claims.FirstOrDefault(claim =>
            claimTypes.Contains(claim.Type, StringComparer.Ordinal)
            && !string.IsNullOrWhiteSpace(claim.Value))?.Value.Trim();

    private static string[] ReadRoles(ClaimsPrincipal principal) =>
        principal.Claims
            .Where(claim => claim.Type is MicrosoftIdentityClaimTypes.Roles
                || claim.Type == ClaimTypes.Role)
            .Select(claim => claim.Value.Trim())
            .Where(role => role.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static bool IdentifiersEqual(string? left, string? right) =>
        Guid.TryParse(left, out var leftGuid)
        && Guid.TryParse(right, out var rightGuid)
        && leftGuid == rightGuid;

    private static string Suffix(string? identifier)
    {
        if (!Guid.TryParse(identifier, out var value))
        {
            return "missing-or-invalid";
        }

        var normalized = value.ToString("N");
        return normalized[^6..];
    }

    private static string SafeRoles(IEnumerable<string> roles) =>
        string.Join(
            '|',
            roles.Select(role => role.Length <= 128
                    && role.All(character => char.IsLetterOrDigit(character)
                        || character is '.' or '-' or '_')
                ? role
                : "invalid-role-value"));

    private sealed record DiagnosticState(
        bool AuthenticationSucceeded,
        bool AudienceValidated,
        bool HasAccessAsUserScope,
        bool TokenTenantMatchesConfiguredTenant,
        string TokenOidSuffix,
        string TokenTidSuffix,
        string TokenRoles,
        bool ReportUserRoleMappingResult,
        bool ReportsCreatePermissionResult,
        string ScopeLookupOidSuffix,
        bool ScopeAssignmentFound,
        bool ScopeIsActive,
        bool AllowAllRegions,
        bool AllowAllStores,
        bool ScopeIntegrityValid,
        bool HasReportUserRole);
}
