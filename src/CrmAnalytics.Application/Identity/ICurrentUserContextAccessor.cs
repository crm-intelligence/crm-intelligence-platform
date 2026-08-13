namespace CrmAnalytics.Application.Identity;

public interface ICurrentUserContextAccessor
{
    AuthenticatedUserContext GetRequiredUser();
}
