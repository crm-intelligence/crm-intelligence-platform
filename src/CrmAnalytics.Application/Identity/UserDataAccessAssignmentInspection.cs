namespace CrmAnalytics.Application.Identity;

public sealed record UserDataAccessAssignmentInspection(
    bool AssignmentFound,
    bool IsActive,
    bool AllowAllRegions,
    bool AllowAllStores,
    bool IntegrityValid)
{
    public static UserDataAccessAssignmentInspection NotFound { get; } =
        new(false, false, false, false, true);

    public static UserDataAccessAssignmentInspection Active(
        UserDataAccessAssignment assignment)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        return new(
            true,
            true,
            assignment.AllowAllRegions,
            assignment.AllowAllStores,
            true);
    }
}
