using CrmAnalytics.Teams.Configuration;
using Microsoft.Extensions.Options;

namespace CrmAnalytics.Teams.Messaging;

internal interface ITeamsUserTokenClient
{
    Task SignOutUserAsync(
        string connectionName,
        CancellationToken cancellationToken);
}

internal sealed class TeamsUserTokenClientAdapter(
    Func<string, CancellationToken, Task> signOutUserAsync)
    : ITeamsUserTokenClient
{
    public Task SignOutUserAsync(
        string connectionName,
        CancellationToken cancellationToken) =>
        signOutUserAsync(connectionName, cancellationToken);
}

internal sealed class TeamsMessageDispatcher
{
    public const string SignOutCompletedMessage =
        "Oturumunuz kapatıldı. Yeni bir rapor talebi gönderdiğinizde "
        + "tekrar oturum açmanız istenecektir.";

    public const string SignOutFailedMessage =
        "Oturumunuz kapatılamadı. Lütfen yeniden deneyin.";

    private readonly string _oauthConnectionName;
    private readonly ILogger<TeamsMessageDispatcher> _logger;

    public TeamsMessageDispatcher(
        IOptions<TeamsUserAuthenticationOptions> authenticationOptions,
        ILogger<TeamsMessageDispatcher> logger)
    {
        ArgumentNullException.ThrowIfNull(authenticationOptions);
        ArgumentNullException.ThrowIfNull(logger);

        _oauthConnectionName = authenticationOptions.Value
            .OAuthConnectionName;
        _logger = logger;
    }

    public async Task DispatchAsync(
        string? message,
        ITeamsUserTokenClient userTokenClient,
        Func<string, CancellationToken, Task> sendReplyAsync,
        Func<CancellationToken, Task> forwardToReportPipelineAsync,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(userTokenClient);
        ArgumentNullException.ThrowIfNull(sendReplyAsync);
        ArgumentNullException.ThrowIfNull(forwardToReportPipelineAsync);

        if (!IsSignOutCommand(message))
        {
            await forwardToReportPipelineAsync(cancellationToken);
            return;
        }

        try
        {
            await userTokenClient.SignOutUserAsync(
                _oauthConnectionName,
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Do not attach the exception: authentication failures can carry
            // credential material in their message or response details.
            _logger.LogWarning("Teams user sign-out failed.");
            await sendReplyAsync(
                SignOutFailedMessage,
                cancellationToken);
            return;
        }

        await sendReplyAsync(
            SignOutCompletedMessage,
            cancellationToken);
    }

    internal static bool IsSignOutCommand(string? message)
    {
        var command = message?.Trim();
        return string.Equals(
                command,
                "logout",
                StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                command,
                "signout",
                StringComparison.OrdinalIgnoreCase);
    }
}
