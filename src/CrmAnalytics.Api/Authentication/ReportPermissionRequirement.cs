using CrmAnalytics.Application.Authorization;
using Microsoft.AspNetCore.Authorization;

namespace CrmAnalytics.Api.Authentication;

public sealed record ReportPermissionRequirement(
    ReportPermission Permission) : IAuthorizationRequirement;
