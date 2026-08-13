namespace CrmAnalytics.Application.Identity;

public interface IUserDataAccessAssignmentStore
{
    UserDataAccessAssignment? Find(
        string tenantId,
        string userId);

    Task<UserDataAccessAssignment?> FindAsync(
        string tenantId,
        string userId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Find(tenantId, userId));
    }

    async Task<UserDataAccessAssignmentInspection> InspectAsync(
        string tenantId,
        string userId,
        CancellationToken cancellationToken)
    {
        var assignment = await FindAsync(
            tenantId,
            userId,
            cancellationToken);
        return assignment is null
            ? UserDataAccessAssignmentInspection.NotFound
            : UserDataAccessAssignmentInspection.Active(assignment);
    }
}
