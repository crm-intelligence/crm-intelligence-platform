using CrmAnalytics.Teams.Configuration;
using CrmAnalytics.Teams.Hosting;
using Microsoft.Teams.Apps;

namespace CrmAnalytics.UnitTests;

public sealed class TeamsUserOAuthRegistrationExtensionsTests
{
    [Fact]
    public void Entra_AddsConfiguredOAuthConnection()
    {
        var app = App.Builder()
            .AddCrmAnalyticsTeamsUserOAuth(
                new TeamsUserAuthenticationOptions
                {
                    Mode = TeamsUserAuthenticationMode.Entra,
                    OAuthConnectionName = "crm-analytics-sso"
                })
            .Build();

        Assert.NotNull(app.OAuth);
        Assert.Equal(
            "crm-analytics-sso",
            app.OAuth.DefaultConnectionName);
    }

    [Fact]
    public void Development_DoesNotAddOAuthConnection()
    {
        var sdkDefault = App.Builder()
            .Build()
            .OAuth.DefaultConnectionName;
        var app = App.Builder()
            .AddCrmAnalyticsTeamsUserOAuth(
                new TeamsUserAuthenticationOptions
                {
                    Mode = TeamsUserAuthenticationMode.Development
                })
            .Build();

        Assert.Equal(
            sdkDefault,
            app.OAuth.DefaultConnectionName);
    }

    [Fact]
    public void OAuthOptions_UseConfiguredConnectionAndSafeText()
    {
        var oauth = TeamsUserSignInOptionsFactory.Create(
            new TeamsUserAuthenticationOptions
            {
                Mode = TeamsUserAuthenticationMode.Entra,
                OAuthConnectionName = "connection"
            });

        Assert.Equal("connection", oauth.ConnectionName);
        Assert.Equal(
            TeamsUserSignInOptionsFactory.OAuthCardText,
            oauth.OAuthCardText);
        Assert.Equal(
            TeamsUserSignInOptionsFactory.SignInButtonText,
            oauth.SignInButtonText);
    }
}
