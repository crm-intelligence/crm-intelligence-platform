using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CrmAnalytics.Application.Notifications;
using CrmAnalytics.Contracts.Notifications;
using CrmAnalytics.Contracts.InternalTeams;
using Microsoft.Extensions.Options;

namespace CrmAnalytics.Infrastructure.Notifications;

public sealed class TeamsReportStatusNotificationClient
    : IReportStatusNotificationClient, IInternalTeamsNotificationClient
{
    public const string HttpClientName =
        "CrmAnalytics.Api.TeamsNotifications";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TeamsNotificationOptions _options;

    public TeamsReportStatusNotificationClient(
        IHttpClientFactory httpClientFactory,
        IOptions<TeamsNotificationOptions> options)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(options);

        _httpClientFactory = httpClientFactory;
        _options = options.Value;
    }

    public async Task NotifyAsync(
        ReportStatusNotificationRequest notification,
        CancellationToken cancellationToken)
        => await NotifyCoreAsync(notification, cancellationToken);

    public async Task NotifyAsync(
        InternalReportStatusNotificationRequest notification,
        CancellationToken cancellationToken)
        => await NotifyCoreAsync(notification, cancellationToken);

    private async Task NotifyCoreAsync<TNotification>(
        TNotification notification,
        CancellationToken cancellationToken) where TNotification : class
    {
        ArgumentNullException.ThrowIfNull(notification);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_options.Enabled)
        {
            return;
        }

        var retryNumber = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var request = CreateRequest(notification);
                using var response = await _httpClientFactory
                    .CreateClient(HttpClientName)
                    .SendAsync(request, cancellationToken);

                if (response.StatusCode == HttpStatusCode.NoContent)
                {
                    return;
                }

                if (response.StatusCode == HttpStatusCode.Conflict
                    && retryNumber
                        < _options.TargetNotReadyRetryCount
                    && await IsTargetNotReadyAsync(
                        response,
                        cancellationToken))
                {
                    var delay = GetRetryDelay(retryNumber);
                    retryNumber++;
                    await Task.Delay(delay, cancellationToken);
                    continue;
                }

                throw new TeamsNotificationException(
                    response.StatusCode);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (TeamsNotificationException)
            {
                throw;
            }
            catch (Exception exception)
                when (exception is HttpRequestException
                    or TaskCanceledException
                    or TimeoutException)
            {
                throw new TeamsNotificationException(exception);
            }
        }
    }

    private HttpRequestMessage CreateRequest<TNotification>(
        TNotification notification) where TNotification : class
    {
        var baseUri = new Uri(_options.BaseUrl, UriKind.Absolute);
        var endpoint = new Uri(
            baseUri,
            ReportNotificationHttpConstants.CallbackPath);
        var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(notification)
        };
        request.Headers.TryAddWithoutValidation(
            ReportNotificationHttpConstants.ApiKeyHeaderName,
            _options.ApiKey);
        return request;
    }

    private static async Task<bool> IsTargetNotReadyAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var error = await response.Content.ReadFromJsonAsync<
                NotificationErrorResponse>(
                    cancellationToken: cancellationToken);
            return string.Equals(
                error?.ErrorCode,
                ReportNotificationHttpConstants.TargetNotReadyErrorCode,
                StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    private TimeSpan GetRetryDelay(int retryNumber)
    {
        var multiplier = 1L << Math.Min(retryNumber, 10);
        var milliseconds = Math.Min(
            (long)_options.TargetNotReadyInitialDelayMilliseconds
                * multiplier,
            5000L);
        return TimeSpan.FromMilliseconds(milliseconds);
    }

    private sealed record NotificationErrorResponse(string ErrorCode);
}
