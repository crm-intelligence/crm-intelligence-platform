using CrmAnalytics.Teams.Authentication;
using CrmAnalytics.Teams.Configuration;
using CrmAnalytics.Teams.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Teams.Apps;
using Microsoft.Teams.Apps.Activities;

namespace CrmAnalytics.Teams.Hosting;

public static class TeamsMessageRegistrationExtensions
{
    public static App MapCrmAnalyticsReportRequests(
        this App teams,
        IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(teams);
        ArgumentNullException.ThrowIfNull(serviceProvider);

        var scopeFactory = serviceProvider
            .GetRequiredService<IServiceScopeFactory>();
        var logger = serviceProvider.GetRequiredService<
            ILoggerFactory>().CreateLogger(
                typeof(TeamsMessageRegistrationExtensions));
        var authenticationOptions = serviceProvider
            .GetRequiredService<
                IOptions<TeamsUserAuthenticationOptions>>()
            .Value;
        var messageDispatcher = serviceProvider
            .GetRequiredService<TeamsMessageDispatcher>();

        teams.OnMessage(async (context, cancellationToken) =>
        {
            var prompt = context.Activity.Text;
            var userTokenClient = new TeamsUserTokenClientAdapter(
                (connectionName, tokenCancellation) =>
                    context.SignOut(
                        connectionName,
                        tokenCancellation));

            await messageDispatcher.DispatchAsync(
                prompt,
                userTokenClient,
                (message, tokenCancellation) =>
                    context.Send(message, tokenCancellation),
                ProcessReportRequestAsync,
                cancellationToken);

            async Task ProcessReportRequestAsync(
                CancellationToken reportCancellation)
            {
                var conversationId = context.Activity.Conversation.Id;
                var validationFailure = TeamsReportRequestHandler.Validate(
                    prompt,
                    conversationId);
                if (validationFailure is not null)
                {
                    await context.Send(
                        validationFailure.Message,
                        reportCancellation);
                    return;
                }

                if (TeamsReportRequestHandler.CanSendTyping(
                    prompt,
                    conversationId))
                {
                    try
                    {
                        await context.Typing(
                            cancellationToken: reportCancellation);
                    }
                    catch (OperationCanceledException)
                        when (reportCancellation.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        logger.LogWarning(
                            exception,
                            "Could not send Teams typing activity.");
                    }
                }

                await using var scope = scopeFactory.CreateAsyncScope();
                var authorizationCoordinator = scope.ServiceProvider
                    .GetRequiredService<
                        ITeamsBackendAuthorizationCoordinator>();
                var authorizationResult =
                    await authorizationCoordinator.AuthorizeAsync(
                        AcquireTokenAsync,
                        reportCancellation);
                if (!authorizationResult.IsAuthorized)
                {
                    return;
                }

                var handler = scope.ServiceProvider
                    .GetRequiredService<TeamsReportRequestHandler>();
                var result = await handler.HandleAsync(
                    prompt,
                    conversationId,
                    authorizationResult.Authorization!,
                    reportCancellation);

                await context.Send(result.Message, reportCancellation);

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
            }
        });

        return teams;
    }
}
