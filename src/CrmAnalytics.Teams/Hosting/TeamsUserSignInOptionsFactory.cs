using CrmAnalytics.Teams.Configuration;
using Microsoft.Teams.Apps;

namespace CrmAnalytics.Teams.Hosting;

public static class TeamsUserSignInOptionsFactory
{
    public const string OAuthCardText =
        "Raporlara erişebilmek için kurumsal hesabınızla oturum açın.";

    public const string SignInButtonText = "Oturum aç";

    public static OAuthOptions Create(
        TeamsUserAuthenticationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            options.OAuthConnectionName);

        return new OAuthOptions
        {
            ConnectionName = options.OAuthConnectionName,
            OAuthCardText = OAuthCardText,
            SignInButtonText = SignInButtonText
        };
    }
}
