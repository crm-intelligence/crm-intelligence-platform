using CrmAnalytics.Teams.Configuration;
using Microsoft.Teams.Apps;

namespace CrmAnalytics.Teams.Hosting;

public static class TeamsUserOAuthRegistrationExtensions
{
    public static AppBuilder AddCrmAnalyticsTeamsUserOAuth(
        this AppBuilder appBuilder,
        TeamsUserAuthenticationOptions options)
    {
        ArgumentNullException.ThrowIfNull(appBuilder);
        ArgumentNullException.ThrowIfNull(options);

        if (options.Mode == TeamsUserAuthenticationMode.Entra)
        {
            if (!string.IsNullOrWhiteSpace(
                    options.OAuthConnectionName))
            {
                appBuilder.AddOAuth(options.OAuthConnectionName);
            }
        }

        return appBuilder;
    }
}
