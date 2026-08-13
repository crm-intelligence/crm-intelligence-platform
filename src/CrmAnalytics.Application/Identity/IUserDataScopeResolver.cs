using CrmAnalytics.Contracts.Integrations;

namespace CrmAnalytics.Application.Identity;

public interface IUserDataScopeResolver
{
    UserDataScope ResolveRequired(AuthenticatedUserContext user);

    Task<UserDataScope> ResolveRequiredAsync(
        AuthenticatedUserContext user,
        CancellationToken cancellationToken);

    UserDataScope Resolve(AuthenticatedUserContext user) =>
        ResolveRequired(user);
}
