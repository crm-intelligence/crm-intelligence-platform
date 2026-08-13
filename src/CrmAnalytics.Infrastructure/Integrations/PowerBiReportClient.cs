using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using CrmAnalytics.Application.Abstractions.Integrations;
using CrmAnalytics.Contracts.Integrations;
using Microsoft.Extensions.Options;

namespace CrmAnalytics.Infrastructure.Integrations;

public sealed record PowerBiReportMetadata(string ReportId, Uri WebUrl);

public interface IPowerBiReportApiClient
{
    Task<PowerBiReportMetadata> GetConfiguredReportAsync(
        CancellationToken cancellationToken);

    Task RefreshSemanticModelAsync(CancellationToken cancellationToken);
}

public sealed class PowerBiReportApiClient : IPowerBiReportApiClient
{
    public const string HttpClientName = "PowerBiApi";
    private static readonly Uri ApiRoot =
        new("https://api.powerbi.com/");
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IPowerBiAccessTokenProvider _tokenProvider;
    private readonly PowerBiOptions _options;

    public PowerBiReportApiClient(
        IHttpClientFactory httpClientFactory,
        IPowerBiAccessTokenProvider tokenProvider,
        IOptions<ReportingOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _tokenProvider = tokenProvider;
        _options = options.Value.PowerBi;
    }

    public async Task<PowerBiReportMetadata> GetConfiguredReportAsync(
        CancellationToken cancellationToken)
    {
        var uri = BuildUri(
            $"groups/{Guid.Parse(_options.WorkspaceId):D}/reports/{Guid.Parse(_options.ReportId):D}");
        using var response = await SendWithRetryAsync(
            HttpMethod.Get, uri, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw CreateFailure(response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync(
            cancellationToken);
        using var json = await JsonDocument.ParseAsync(
            stream, cancellationToken: cancellationToken);
        var root = json.RootElement;
        var id = root.TryGetProperty("id", out var idValue)
            ? idValue.GetString()
            : null;
        var webUrl = root.TryGetProperty("webUrl", out var urlValue)
            ? urlValue.GetString()
            : null;
        if (!Guid.TryParse(id, out var parsedId)
            || parsedId != Guid.Parse(_options.ReportId)
            || !TryValidateWebUrl(webUrl, out var safeUri))
            throw new InvalidOperationException(
                "Power BI returned invalid report metadata.");

        return new PowerBiReportMetadata(parsedId.ToString("D"), safeUri);
    }

    public async Task RefreshSemanticModelAsync(
        CancellationToken cancellationToken)
    {
        if (!_options.RefreshBeforeReturn)
            return;

        var datasetId = Guid.Parse(_options.SemanticModelId).ToString("D");
        var root = $"groups/{Guid.Parse(_options.WorkspaceId):D}/datasets/{datasetId}/refreshes";
        using var accepted = await SendWithRetryAsync(
            HttpMethod.Post, BuildUri(root), cancellationToken);
        if (accepted.StatusCode != HttpStatusCode.Accepted)
            throw CreateFailure(accepted.StatusCode);
        if (!_options.RefreshPollingEnabled)
            return;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(
            _options.RefreshTimeoutSeconds));
        try
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(
                    _options.RefreshPollingIntervalSeconds), timeout.Token);
                using var response = await SendWithRetryAsync(
                    HttpMethod.Get, BuildUri(root + "?$top=1"), timeout.Token);
                if (!response.IsSuccessStatusCode)
                    throw CreateFailure(response.StatusCode);
                var status = await ReadLatestRefreshStatusAsync(
                    response, timeout.Token);
                if (Is(status, "Completed"))
                    return;
                if (Is(status, "Failed") || Is(status, "Cancelled")
                    || Is(status, "Disabled"))
                    throw new InvalidOperationException(
                        "Power BI semantic model refresh failed.");
            }
        }
        catch (OperationCanceledException) when (
            !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                "Power BI semantic model refresh timed out.");
        }
    }

    public static bool TryValidateWebUrl(string? value, out Uri uri)
    {
        if (Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var candidate)
            && string.Equals(candidate.Scheme, Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase)
            && string.Equals(candidate.Host, "app.powerbi.com",
                StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrEmpty(candidate.UserInfo)
            && candidate.IsWellFormedOriginalString())
        {
            uri = candidate;
            return true;
        }

        uri = null!;
        return false;
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(
        HttpMethod method, Uri uri, CancellationToken cancellationToken)
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

    private static async Task<string?> ReadLatestRefreshStatusAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(
            cancellationToken);
        using var json = await JsonDocument.ParseAsync(
            stream, cancellationToken: cancellationToken);
        if (!json.RootElement.TryGetProperty("value", out var values)
            || values.ValueKind != JsonValueKind.Array
            || values.GetArrayLength() == 0)
            return null;
        var latest = values[0];
        return latest.TryGetProperty("status", out var status)
            ? status.GetString()
            : null;
    }

    private static Uri BuildUri(string path) =>
        new(ApiRoot, "v1.0/myorg/" + path);

    private static Exception CreateFailure(HttpStatusCode statusCode) =>
        new HttpRequestException(
            $"Power BI API request failed with status {(int)statusCode}.",
            null, statusCode);

    private static bool Is(string? value, string expected) =>
        string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);
}

public sealed class PowerBiReportClient : IReportClient
{
    private readonly IPowerBiReportApiClient _apiClient;
    private readonly PowerBiOptions _options;

    public PowerBiReportClient(
        IPowerBiReportApiClient apiClient,
        IOptions<ReportingOptions> options)
    {
        _apiClient = apiClient;
        _options = options.Value.PowerBi;
    }

    public async Task<ReportGenerationResponse> GenerateAsync(
        ReportGenerationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            await _apiClient.RefreshSemanticModelAsync(cancellationToken);
            var report = await _apiClient.GetConfiguredReportAsync(
                cancellationToken);
            return new ReportGenerationResponse(
                report.ReportId, null, report.WebUrl.AbsoluteUri,
                ExternalOperationStatus.Completed, null);
        }
        catch (OperationCanceledException) { throw; }
        catch (TimeoutException) { throw; }
        catch (Exception)
        {
            return new ReportGenerationResponse(
                null, null, null, ExternalOperationStatus.Failed,
                new ExternalServiceError(
                    "POWER_BI_REPORT_FAILED",
                    "The configured Power BI report is unavailable.",
                    false));
        }
    }
}
