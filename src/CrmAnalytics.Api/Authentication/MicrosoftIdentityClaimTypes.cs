namespace CrmAnalytics.Api.Authentication;

public static class MicrosoftIdentityClaimTypes
{
    public const string ObjectId = "oid";

    public const string ObjectIdUri =
        "http://schemas.microsoft.com/identity/claims/objectidentifier";

    public const string TenantId = "tid";

    public const string TenantIdUri =
        "http://schemas.microsoft.com/identity/claims/tenantid";

    public const string Scope = "scp";

    public const string ScopeUri =
        "http://schemas.microsoft.com/identity/claims/scope";

    public const string Roles = "roles";

    public const string IdentityType = "idtyp";
}
