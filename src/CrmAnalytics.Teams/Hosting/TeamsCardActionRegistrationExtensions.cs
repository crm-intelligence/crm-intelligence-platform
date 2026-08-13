using CrmAnalytics.Teams.Authentication;
using CrmAnalytics.Teams.Configuration;
using CrmAnalytics.Teams.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Teams.Api;
using Microsoft.Teams.Api.AdaptiveCards;
using Microsoft.Teams.Apps.Activities.Invokes;
using TeamsApp = Microsoft.Teams.Apps.App;

namespace CrmAnalytics.Teams.Hosting;

public static class TeamsCardActionRegistrationExtensions
{
    public static TeamsApp MapCrmAnalyticsCardActions(
        this TeamsApp teams,
        IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(teams);
        ArgumentNullException.ThrowIfNull(serviceProvider);

        var scopeFactory = serviceProvider
            .GetRequiredService<IServiceScopeFactory>();
        var authenticationOptions = serviceProvider
            .GetRequiredService<
                IOptions<TeamsUserAuthenticationOptions>>()
            .Value;

        teams.OnAdaptiveCardAction(
            async (context, cancellationToken) =>
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var authorizationCoordinator = scope.ServiceProvider
                    .GetRequiredService<
                        ITeamsBackendAuthorizationCoordinator>();
                var authorizationResult =
                    await authorizationCoordinator.AuthorizeAsync(
                        tokenCancellation =>
                            AcquireTokenAsync(tokenCancellation),
                        cancellationToken);
                if (!authorizationResult.IsAuthorized)
                {
                    return new ActionResponse(ContentType.AdaptiveCard)
                    {
                        StatusCode = StatusCodes.Status200OK,
                        Value = TeamsReportCardActionHandler
                            .CreateSignInRequiredCard()
                    };
                }

                var handler = scope.ServiceProvider.GetRequiredService<
                    ITeamsReportCardActionHandler>();
                var action = context.Activity.Value.Action;
                var data = action?.Data is null
                    ? new Dictionary<string, object?>()
                    : action.Data.ToDictionary(
                        pair => pair.Key,
                        pair => (object?)pair.Value,
                        StringComparer.Ordinal);
                var card = await handler.HandleAsync(
                    action?.Verb,
                    data,
                    context.Activity.Conversation?.Id,
                    authorizationResult.Authorization!,
                    cancellationToken);

                return new ActionResponse(ContentType.AdaptiveCard)
                {
                    StatusCode = StatusCodes.Status200OK,
                    Value = card
                };

                async Task<string?> AcquireTokenAsync(
                    CancellationToken tokenCancellation)
                {
                    var oauthOptions =
                        TeamsUserSignInOptionsFactory.Create(
                            authenticationOptions);
                    return await context.SignIn(
                        oauthOptions,
                        tokenCancellation);
                }
            });

        return teams;
    }
}
