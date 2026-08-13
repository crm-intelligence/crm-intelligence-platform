namespace CrmAnalytics.Application.Identity;

public sealed record AuthenticatedUserContext
{
    public AuthenticatedUserContext(
        string userId,
        string tenantId,
        IReadOnlyCollection<string> roles)
    {
        UserId = NormalizeIdentifier(userId, nameof(userId));
        TenantId = NormalizeIdentifier(tenantId, nameof(tenantId));
        ArgumentNullException.ThrowIfNull(roles);
        Roles = Array.AsReadOnly(roles
            .Where(role => !string.IsNullOrWhiteSpace(role))
            .Select(role => role.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray());
    }

    public string UserId { get; }

    public string TenantId { get; }

    public IReadOnlyCollection<string> Roles { get; }

    private static string NormalizeIdentifier(
        string value,
        string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);

        if (!Guid.TryParse(value.Trim(), out var identifier))
        {
            throw new ArgumentException(
                "The identity value must be a valid GUID.",
                parameterName);
        }

        return identifier.ToString("D");
    }
}
