using System.Security.Claims;

namespace CrmAnalytics.Api.Authentication;

public static class MicrosoftIdentityScopeResolver
{
    private static readonly string[] SupportedClaimTypes =
    [
        MicrosoftIdentityClaimTypes.Scope,
        MicrosoftIdentityClaimTypes.ScopeUri
    ];

    public static IReadOnlyCollection<string> Resolve(
        ClaimsPrincipal principal) =>
        principal.Claims
            .Where(claim => SupportedClaimTypes.Contains(
                claim.Type,
                StringComparer.Ordinal))
            .SelectMany(claim => claim.Value.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries
                    | StringSplitOptions.TrimEntries))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    public static bool HasScope(
        ClaimsPrincipal principal,
        string requiredScope)
    {
        var normalizedRequiredScope = requiredScope.Trim();
        return normalizedRequiredScope.Length > 0
            && Resolve(principal).Contains(
                normalizedRequiredScope,
                StringComparer.Ordinal);
    }
}
