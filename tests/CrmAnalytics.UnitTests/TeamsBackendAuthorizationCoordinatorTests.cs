using System.Text.Json;
using CrmAnalytics.Teams.Authentication;
using CrmAnalytics.Teams.Configuration;
using Microsoft.Extensions.Options;

namespace CrmAnalytics.UnitTests;

public sealed class TeamsBackendAuthorizationCoordinatorTests
{
    [Fact]
    public async Task Development_DoesNotAcquireToken()
    {
        var calls = 0;
        var coordinator = CreateCoordinator(
            TeamsUserAuthenticationMode.Development);

        var result = await coordinator.AuthorizeAsync(
            _ =>
            {
                calls++;
                return Task.FromResult<string?>("must-not-be-used");
            },
            CancellationToken.None);

        Assert.Equal(0, calls);
        Assert.True(result.IsAuthorized);
        Assert.NotNull(result.Authorization);
        Assert.False(result.Authorization.RequiresBearerToken);
        Assert.Null(result.Authorization.AccessToken);
    }

    [Fact]
    public async Task Entra_AcquiresOpaqueTokenOnceWithoutChangingIt()
    {
        const string token = "  opaque.token/value  ";
        var calls = 0;
        var coordinator = CreateCoordinator(
            TeamsUserAuthenticationMode.Entra);

        var result = await coordinator.AuthorizeAsync(
            _ =>
            {
                calls++;
                return Task.FromResult<string?>(token);
            },
            CancellationToken.None);

        Assert.Equal(1, calls);
        Assert.True(result.IsAuthorized);
        Assert.Same(token, result.Authorization!.AccessToken);
        Assert.True(result.Authorization.RequiresBearerToken);
    }

    [Fact]
    public async Task Entra_NullToken_ReturnsSignInStarted()
    {
        var coordinator = CreateCoordinator(
            TeamsUserAuthenticationMode.Entra);

        var result = await coordinator.AuthorizeAsync(
            _ => Task.FromResult<string?>(null),
            CancellationToken.None);

        Assert.Equal(
            BackendAuthorizationStatus.SignInStarted,
            result.Status);
        Assert.Null(result.Authorization);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Entra_BlankToken_IsRejectedWithoutDisclosure(
        string token)
    {
        var coordinator = CreateCoordinator(
            TeamsUserAuthenticationMode.Entra);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.AuthorizeAsync(
                _ => Task.FromResult<string?>(token),
                CancellationToken.None));

        Assert.Equal(
            "Teams sign-in returned an invalid access token.",
            exception.Message);
    }

    [Fact]
    public async Task Entra_PassesCancellationToAcquisition()
    {
        using var source = new CancellationTokenSource();
        var observed = default(CancellationToken);
        var coordinator = CreateCoordinator(
            TeamsUserAuthenticationMode.Entra);

        await coordinator.AuthorizeAsync(
            token =>
            {
                observed = token;
                return Task.FromResult<string?>("opaque");
            },
            source.Token);

        Assert.Equal(source.Token, observed);
    }

    [Fact]
    public async Task Entra_PreCancelledToken_DoesNotInvokeAcquisition()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        var calls = 0;
        var coordinator = CreateCoordinator(
            TeamsUserAuthenticationMode.Entra);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => coordinator.AuthorizeAsync(
                _ =>
                {
                    calls++;
                    return Task.FromResult<string?>("opaque");
                },
                source.Token));

        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task ResultAndAuthorizationToString_DoNotExposeToken()
    {
        const string token = "opaque-secret-token";
        var result = await CreateCoordinator(
                TeamsUserAuthenticationMode.Entra)
            .AuthorizeAsync(
                _ => Task.FromResult<string?>(token),
                CancellationToken.None);

        Assert.DoesNotContain(token, result.ToString());
        Assert.DoesNotContain(
            token,
            result.Authorization!.ToString());
    }

    [Fact]
    public void BackendAuthorization_RejectsBlankAndDoesNotSerializeToken()
    {
        Assert.Throws<ArgumentException>(
            () => BackendApiAuthorization.Bearer(" "));

        const string token = "opaque-secret-token";
        var json = JsonSerializer.Serialize(
            BackendApiAuthorization.Bearer(token));

        Assert.DoesNotContain(token, json);
    }

    private static TeamsBackendAuthorizationCoordinator
        CreateCoordinator(TeamsUserAuthenticationMode mode) =>
        new(Options.Create(new TeamsUserAuthenticationOptions
        {
            Mode = mode,
            OAuthConnectionName = mode
                == TeamsUserAuthenticationMode.Entra
                    ? "connection"
                    : string.Empty
        }));
}
