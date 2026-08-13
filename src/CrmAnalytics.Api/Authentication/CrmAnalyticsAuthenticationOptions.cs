namespace CrmAnalytics.Api.Authentication;

public sealed class CrmAnalyticsAuthenticationOptions
{
    public const string SectionName = "CrmAnalyticsAuthentication";

    public string Mode { get; set; } = AuthenticationModes.Entra;

    public string RequiredScope { get; set; } = "access_as_user";

    public DevelopmentIdentityOptions Development { get; set; } = new();
}

public sealed class DevelopmentIdentityOptions
{
    public string UserId { get; set; } = string.Empty;

    public string TenantId { get; set; } = string.Empty;

    public string[]? Roles { get; set; } = Array.Empty<string>();
}

public static class AuthenticationModes
{
    public const string Entra = "Entra";

    public const string Development = "Development";
}
