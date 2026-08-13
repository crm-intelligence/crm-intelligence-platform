using CrmAnalytics.Contracts.Integrations;
using CrmAnalytics.Application.Exceptions;

namespace CrmAnalytics.Application.Identity;

public sealed class CurrentUserDataScopeResolver : IUserDataScopeResolver
{
    private readonly IUserDataAccessAssignmentStore _assignmentStore;

    public CurrentUserDataScopeResolver(
        IUserDataAccessAssignmentStore assignmentStore)
    {
        ArgumentNullException.ThrowIfNull(assignmentStore);
        _assignmentStore = assignmentStore;
    }

    public UserDataScope ResolveRequired(AuthenticatedUserContext user)
    {
        ArgumentNullException.ThrowIfNull(user);

        var assignment = _assignmentStore.Find(
            user.TenantId,
            user.UserId);

        return CreateScope(user, assignment);
    }

    public async Task<UserDataScope> ResolveRequiredAsync(
        AuthenticatedUserContext user,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        var assignment = await _assignmentStore.FindAsync(
            user.TenantId,
            user.UserId,
            cancellationToken);

        return CreateScope(user, assignment);
    }

    private static UserDataScope CreateScope(
        AuthenticatedUserContext user,
        UserDataAccessAssignment? assignment)
    {

        if (assignment is null)
        {
            throw new ForbiddenAccessException();
        }

        var scope = new UserDataScope
        {
            UserId = user.UserId,
            TenantId = user.TenantId,
            Roles = user.Roles.ToArray(),
            AllowAllRegions = assignment.AllowAllRegions,
            AllowAllStores = assignment.AllowAllStores,
            AllowedRegions = assignment.AllowedRegions.ToArray(),
            AllowedStoreIds = assignment.AllowedStoreIds.ToArray()
        };

        return UserDataScopeValidator.CreateRequiredSnapshot(scope, user);
    }
}
