using CrmAnalytics.Teams.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Teams.Apps.Activities.Invokes;
using Microsoft.Teams.Apps.Events;
using TeamsApp = Microsoft.Teams.Apps.App;

namespace CrmAnalytics.Teams.Hosting;

public static class TeamsSignInRegistrationExtensions
{
    public const string SignInCompletedMessage =
        "Oturum açma işlemi tamamlandı. Lütfen rapor talebinizi "
        + "yeniden gönderin.";

    public const string SignInFailedMessage =
        "Oturum açma işlemi tamamlanamadı. Lütfen yeniden deneyin "
        + "veya sistem yöneticinizle iletişime geçin.";

    public static TeamsApp MapCrmAnalyticsSignInEvents(
        this TeamsApp teams,
        IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(teams);
        ArgumentNullException.ThrowIfNull(serviceProvider);

        var options = serviceProvider
            .GetRequiredService<
                IOptions<TeamsUserAuthenticationOptions>>()
            .Value;
        if (options.Mode != TeamsUserAuthenticationMode.Entra)
        {
            return teams;
        }

        var logger = serviceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(TeamsSignInRegistrationExtensions));

        teams.OnSignIn(
            async (_, signInEvent, cancellationToken) =>
            {
                await signInEvent.Context.Send(
                    SignInCompletedMessage,
                    cancellationToken);
            });

        teams.OnSignInFailure(
            async (context, cancellationToken) =>
            {
                logger.LogWarning(
                    "Teams user sign-in failed with code "
                        + "{FailureCode}.",
                    context.Activity.Value?.Code ?? "unknown");

                await context.Send(
                    SignInFailedMessage,
                    cancellationToken);
            });

        return teams;
    }
}
