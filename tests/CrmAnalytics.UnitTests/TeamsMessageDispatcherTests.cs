using CrmAnalytics.Teams.Configuration;
using CrmAnalytics.Teams.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CrmAnalytics.UnitTests;

public sealed class TeamsMessageDispatcherTests
{
    [Fact]
    public async Task LogoutCommand_CallsSignOutWithConfiguredConnection()
    {
        var tokenClient = new RecordingUserTokenClient();
        var replies = new List<string>();
        var reportPipelineCalls = 0;

        await CreateDispatcher().DispatchAsync(
            "logout",
            tokenClient,
            (message, _) =>
            {
                replies.Add(message);
                return Task.CompletedTask;
            },
            _ =>
            {
                reportPipelineCalls++;
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.Equal(
            ExpectedConnectionName,
            Assert.Single(tokenClient.ConnectionNames));
        Assert.Equal(
            TeamsMessageDispatcher.SignOutCompletedMessage,
            Assert.Single(replies));
        Assert.Equal(0, reportPipelineCalls);
    }

    [Fact]
    public async Task SignoutCommand_CallsSignOut()
    {
        var tokenClient = new RecordingUserTokenClient();

        await DispatchAsync("signout", tokenClient);

        Assert.Single(tokenClient.ConnectionNames);
    }

    [Theory]
    [InlineData(" LOGOUT ")]
    [InlineData("Logout")]
    [InlineData("SiGnOuT")]
    public async Task SignOutCommand_IsTrimmedAndCaseInsensitive(
        string message)
    {
        var tokenClient = new RecordingUserTokenClient();

        await DispatchAsync(message, tokenClient);

        Assert.Single(tokenClient.ConnectionNames);
    }

    [Fact]
    public async Task NormalReportMessage_DoesNotCallSignOut()
    {
        var tokenClient = new RecordingUserTokenClient();
        var reportPipelineCalls = 0;

        await CreateDispatcher().DispatchAsync(
            "Bu ayın satış raporunu hazırla",
            tokenClient,
            (_, _) => Task.CompletedTask,
            _ =>
            {
                reportPipelineCalls++;
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.Empty(tokenClient.ConnectionNames);
        Assert.Equal(1, reportPipelineCalls);
    }

    [Theory]
    [InlineData("logout")]
    [InlineData("signout")]
    public async Task SignOutMessage_IsNotForwardedToBackendApi(
        string message)
    {
        var backendApiCalls = 0;

        await CreateDispatcher().DispatchAsync(
            message,
            new RecordingUserTokenClient(),
            (_, _) => Task.CompletedTask,
            _ =>
            {
                backendApiCalls++;
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.Equal(0, backendApiCalls);
    }

    private const string ExpectedConnectionName =
        "crm-analytics-teams-oauth";

    private static TeamsMessageDispatcher CreateDispatcher() =>
        new(
            Options.Create(new TeamsUserAuthenticationOptions
            {
                Mode = TeamsUserAuthenticationMode.Entra,
                OAuthConnectionName = ExpectedConnectionName
            }),
            NullLogger<TeamsMessageDispatcher>.Instance);

    private static Task DispatchAsync(
        string message,
        RecordingUserTokenClient tokenClient) =>
        CreateDispatcher().DispatchAsync(
            message,
            tokenClient,
            (_, _) => Task.CompletedTask,
            _ => Task.CompletedTask,
            CancellationToken.None);

    private sealed class RecordingUserTokenClient
        : ITeamsUserTokenClient
    {
        public List<string> ConnectionNames { get; } = [];

        public Task SignOutUserAsync(
            string connectionName,
            CancellationToken cancellationToken)
        {
            ConnectionNames.Add(connectionName);
            return Task.CompletedTask;
        }
    }
}
