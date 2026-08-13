using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;

namespace CrmAnalytics.Infrastructure.Integrations;

public interface IFabricAccessTokenProvider
{
    ValueTask<string> GetAccessTokenAsync(CancellationToken cancellationToken);
}

public interface IPowerBiAccessTokenProvider
{
    ValueTask<string> GetAccessTokenAsync(CancellationToken cancellationToken);
}

public sealed class FabricAccessTokenProvider : IFabricAccessTokenProvider
{
    private static readonly string[] Scopes =
        ["https://api.fabric.microsoft.com/.default"];
    private readonly TokenCredential _credential;

    public FabricAccessTokenProvider(IOptions<AnalyticsOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _credential = CreateCredential(
            options.Value.Fabric.AuthenticationMode,
            options.Value.Fabric.ManagedIdentityClientId);
    }

    public async ValueTask<string> GetAccessTokenAsync(
        CancellationToken cancellationToken)
    {
        var token = await _credential.GetTokenAsync(
            new TokenRequestContext(Scopes), cancellationToken);
        return token.Token;
    }

    internal static TokenCredential CreateCredential(
        string authenticationMode,
        string? clientId)
    {
        if (AnalyticsOptionsValidator.Is(
                authenticationMode, "DefaultAzureCredential"))
        {
            return new DefaultAzureCredential(new DefaultAzureCredentialOptions
            {
                ManagedIdentityClientId = string.IsNullOrWhiteSpace(clientId)
                    ? null
                    : clientId.Trim()
            });
        }

        var identity = string.IsNullOrWhiteSpace(clientId)
            ? ManagedIdentityId.SystemAssigned
            : ManagedIdentityId.FromUserAssignedClientId(clientId.Trim());
        return new ManagedIdentityCredential(identity);
    }
}

public sealed class PowerBiAccessTokenProvider : IPowerBiAccessTokenProvider
{
    private static readonly string[] Scopes =
        ["https://analysis.windows.net/powerbi/api/.default"];
    private readonly TokenCredential _credential;

    public PowerBiAccessTokenProvider(IOptions<ReportingOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _credential = FabricAccessTokenProvider.CreateCredential(
            options.Value.PowerBi.AuthenticationMode,
            options.Value.PowerBi.ManagedIdentityClientId);
    }

    public async ValueTask<string> GetAccessTokenAsync(
        CancellationToken cancellationToken)
    {
        var token = await _credential.GetTokenAsync(
            new TokenRequestContext(Scopes), cancellationToken);
        return token.Token;
    }
}
