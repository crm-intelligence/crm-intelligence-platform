namespace CrmAnalytics.Application.Identity;

public sealed record UserDataAccessAssignment
{
    public UserDataAccessAssignment(
        string tenantId,
        string userId,
        bool allowAllRegions,
        bool allowAllStores,
        IReadOnlyCollection<string> allowedRegions,
        IReadOnlyCollection<string> allowedStoreIds)
    {
        TenantId = NormalizeGuid(tenantId, nameof(tenantId));
        UserId = NormalizeGuid(userId, nameof(userId));
        ArgumentNullException.ThrowIfNull(allowedRegions);
        ArgumentNullException.ThrowIfNull(allowedStoreIds);

        AllowedRegions = NormalizeValues(
            allowedRegions,
            nameof(allowedRegions));
        AllowedStoreIds = NormalizeValues(
            allowedStoreIds,
            nameof(allowedStoreIds));
        AllowAllRegions = allowAllRegions;
        AllowAllStores = allowAllStores;

        if (AllowAllRegions && AllowedRegions.Count != 0)
        {
            throw new ArgumentException(
                "All-region access cannot include explicit regions.",
                nameof(allowedRegions));
        }

        if (AllowAllStores && AllowedStoreIds.Count != 0)
        {
            throw new ArgumentException(
                "All-store access cannot include explicit stores.",
                nameof(allowedStoreIds));
        }

        if (!HasAnyAccess)
        {
            throw new ArgumentException(
                "At least one data access dimension is required.");
        }
    }

    public string TenantId { get; }

    public string UserId { get; }

    public bool AllowAllRegions { get; }

    public bool AllowAllStores { get; }

    public IReadOnlyCollection<string> AllowedRegions { get; }

    public IReadOnlyCollection<string> AllowedStoreIds { get; }

    public bool HasAnyAccess =>
        AllowAllRegions
        || AllowAllStores
        || AllowedRegions.Count != 0
        || AllowedStoreIds.Count != 0;

    public override string ToString() =>
        "UserDataAccessAssignment { Redacted }";

    private static string NormalizeGuid(
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

    private static IReadOnlyCollection<string> NormalizeValues(
        IReadOnlyCollection<string> values,
        string parameterName)
    {
        if (values.Any(value => string.IsNullOrWhiteSpace(value)))
        {
            throw new ArgumentException(
                "Data access values cannot be empty.",
                parameterName);
        }

        var normalized = values
            .Select(value => value.Trim())
            .ToArray();

        if (normalized.Any(value =>
                string.Equals(value, "*", StringComparison.Ordinal)))
        {
            throw new ArgumentException(
                "Wildcard data access values are not supported.",
                parameterName);
        }

        return Array.AsReadOnly(normalized
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray());
    }
}
