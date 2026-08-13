using CrmAnalytics.Application.Identity;
using Microsoft.Extensions.Options;

namespace CrmAnalytics.Infrastructure.Identity;

public sealed class ConfigurationUserDataAccessAssignmentStore
    : IUserDataAccessAssignmentStore
{
    private readonly IReadOnlyDictionary<
        (Guid TenantId, Guid UserId),
        UserDataAccessAssignment> _assignments;

    public ConfigurationUserDataAccessAssignmentStore(
        IOptions<ReportDataAccessOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _assignments = (options.Value.Assignments
                ?? throw new InvalidOperationException(
                    "Data access assignments are required."))
            .Select(value => new UserDataAccessAssignment(
                value.TenantId ?? string.Empty,
                value.UserId ?? string.Empty,
                value.AllowAllRegions,
                value.AllowAllStores,
                value.AllowedRegions
                    ?? throw new InvalidOperationException(
                        "AllowedRegions is required."),
                value.AllowedStoreIds
                    ?? throw new InvalidOperationException(
                        "AllowedStoreIds is required.")))
            .ToDictionary(
                assignment => (
                    Guid.Parse(assignment.TenantId),
                    Guid.Parse(assignment.UserId)));
    }

    public UserDataAccessAssignment? Find(
        string tenantId,
        string userId)
    {
        if (!Guid.TryParse(tenantId, out var normalizedTenantId)
            || !Guid.TryParse(userId, out var normalizedUserId))
        {
            return null;
        }

        return _assignments.TryGetValue(
            (normalizedTenantId, normalizedUserId),
            out var assignment)
            ? assignment
            : null;
    }
}
