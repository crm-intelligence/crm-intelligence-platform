using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using CrmAnalytics.Application.Abstractions.Integrations;
using CrmAnalytics.Application.Analytics;
using CrmAnalytics.Contracts.Integrations;
using Microsoft.Extensions.Options;

namespace CrmAnalytics.Infrastructure.Integrations;

public sealed record FabricJobReference(string JobInstanceId);

public interface IFabricJobClient
{
    Task<FabricJobReference> RunToCompletionAsync(
        CancellationToken cancellationToken);
}

public sealed class FabricJobClient : IFabricJobClient
{
    public const string HttpClientName = "FabricApi";
    private static readonly Uri ApiRoot =
        new("https://api.fabric.microsoft.com/");
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IFabricAccessTokenProvider _tokenProvider;
    private readonly FabricOptions _options;

    public FabricJobClient(
        IHttpClientFactory httpClientFactory,
        IFabricAccessTokenProvider tokenProvider,
        IOptions<AnalyticsOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _tokenProvider = tokenProvider;
        _options = options.Value.Fabric;
    }

    public async Task<FabricJobReference> RunToCompletionAsync(
        CancellationToken cancellationToken)
    {
        var workspaceId = Guid.Parse(_options.WorkspaceId).ToString("D");
        var itemId = Guid.Parse(_options.ItemId).ToString("D");
        var jobType = Uri.EscapeDataString(_options.JobType.Trim());
        var path = $"v1/workspaces/{workspaceId}/items/{itemId}/jobs/instances?jobType={jobType}";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

        try
        {
            using var response = await SendWithRetryAsync(
                HttpMethod.Post, new Uri(ApiRoot, path), timeout.Token);
            if (response.StatusCode != HttpStatusCode.Accepted)
                throw CreatePermanent(response.StatusCode);

            var pollingUri = ValidatePollingUri(response.Headers.Location);
            var jobId = pollingUri.Segments.Last().Trim('/');
            while (true)
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(_options.PollingIntervalSeconds),
                    timeout.Token);
                using var poll = await SendWithRetryAsync(
                    HttpMethod.Get, pollingUri, timeout.Token);
                if (!poll.IsSuccessStatusCode)
                    throw CreatePermanent(poll.StatusCode);

                await using var stream = await poll.Content.ReadAsStreamAsync(
                    timeout.Token);
                using var json = await JsonDocument.ParseAsync(
                    stream, cancellationToken: timeout.Token);
                var status = json.RootElement.TryGetProperty("status", out var value)
                    ? value.GetString()
                    : null;
                if (Is(status, "Completed") || Is(status, "Succeeded"))
                    return new FabricJobReference(jobId);
                if (Is(status, "Failed") || Is(status, "Cancelled")
                    || Is(status, "Deduped"))
                    throw new InvalidOperationException(
                        "Fabric job did not complete successfully.");
            }
        }
        catch (OperationCanceledException) when (
            !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Fabric job polling timed out.");
        }
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(
        HttpMethod method,
        Uri uri,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(method, uri);
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer", await _tokenProvider.GetAccessTokenAsync(
                    cancellationToken));
            var response = await _httpClientFactory.CreateClient(HttpClientName)
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
            if (!ExternalHttpRetry.IsTransient(response.StatusCode)
                || attempt >= _options.MaxRetries)
                return response;

            var delay = ExternalHttpRetry.GetDelay(response, attempt);
            response.Dispose();
            await Task.Delay(delay, cancellationToken);
        }
    }

    private static Uri ValidatePollingUri(Uri? location)
    {
        if (location is null || !location.IsAbsoluteUri
            || !string.Equals(location.Scheme, Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(location.Host, ApiRoot.Host,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Fabric returned an invalid operation reference.");
        return location;
    }

    private static Exception CreatePermanent(HttpStatusCode statusCode) =>
        new HttpRequestException(
            $"Fabric API request failed with status {(int)statusCode}.",
            null, statusCode);

    private static bool Is(string? value, string expected) =>
        string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);
}

public sealed class FabricJobAnalyticsClient : IAnalyticsClient
{
    private readonly IFabricJobClient _jobClient;
    private readonly AnalyticsOptions _options;

    public FabricJobAnalyticsClient(
        IFabricJobClient jobClient,
        IOptions<AnalyticsOptions> options)
    {
        _jobClient = jobClient;
        _options = options.Value;
    }

    public async Task<AnalyticsResponse> AnalyzeAsync(
        AnalyticsExecutionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.QueryResult is not null)
        {
            return Failed(request.Request.RequestId,
                "FABRIC_STAGING_CONTRACT_MISSING",
                "Fabric analytics is unavailable because durable result staging is not configured.");
        }

        try
        {
            var result = await _jobClient.RunToCompletionAsync(cancellationToken);
            var scenario = string.IsNullOrWhiteSpace(_options.Fabric.ScenarioKey)
                ? "configured"
                : _options.Fabric.ScenarioKey.Trim();
            return new AnalyticsResponse(
                result.JobInstanceId,
                ExternalOperationStatus.Completed,
                $"fabric-job://{result.JobInstanceId}",
                $"Fabric scenario '{scenario}' completed.",
                null);
        }
        catch (OperationCanceledException) { throw; }
        catch (TimeoutException) { throw; }
        catch (Exception)
        {
            return Failed(request.Request.RequestId, "FABRIC_JOB_FAILED",
                "Fabric analytics could not be completed.");
        }
    }

    private static AnalyticsResponse Failed(
        string requestId, string code, string message) =>
        new($"fabric-{requestId}", ExternalOperationStatus.Failed,
            null, null, new ExternalServiceError(code, message, false));
}
