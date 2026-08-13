namespace CrmAnalytics.Contracts.Integrations;

public sealed record UserDataScope
{
    public static UserDataScope Empty { get; } = new();

    private IReadOnlyCollection<string> _roles =
        Array.Empty<string>();
    private IReadOnlyCollection<string> _allowedRegions =
        Array.Empty<string>();
    private IReadOnlyCollection<string> _allowedStoreIds =
        Array.Empty<string>();

    public string? UserId { get; init; }

    public string? TenantId { get; init; }

    public bool AllowAllRegions { get; init; }

    public bool AllowAllStores { get; init; }

    public IReadOnlyCollection<string> Roles
    {
        get => _roles;
        init => _roles = Normalize(value, StringComparer.Ordinal);
    }

    public IReadOnlyCollection<string> AllowedRegions
    {
        get => _allowedRegions;
        init => _allowedRegions = Normalize(
            value,
            StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<string> AllowedStoreIds
    {
        get => _allowedStoreIds;
        init => _allowedStoreIds = Normalize(
            value,
            StringComparer.OrdinalIgnoreCase);
    }

    public override string ToString() => "UserDataScope { Redacted }";

    private static IReadOnlyCollection<string> Normalize(
        IReadOnlyCollection<string>? values,
        StringComparer comparer)
    {
        var normalized = values?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(comparer)
            .ToArray()
            ?? Array.Empty<string>();

        return Array.AsReadOnly(normalized);
    }
}
