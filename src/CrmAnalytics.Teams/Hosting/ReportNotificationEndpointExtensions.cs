using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CrmAnalytics.Contracts.Notifications;
using CrmAnalytics.Contracts.InternalTeams;
using CrmAnalytics.Teams.Configuration;
using CrmAnalytics.Teams.Notifications;
using Microsoft.Extensions.Options;

namespace CrmAnalytics.Teams.Hosting;

public static class ReportNotificationEndpointExtensions
{
    public static IEndpointConventionBuilder MapCrmAnalyticsReportNotifications(
        this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        return endpoints.MapPost(ReportNotificationHttpConstants.CallbackPath,
            HandleAsync);
    }

    internal static async Task<IResult> HandleAsync(
        HttpContext httpContext,
        IOptions<ReportNotificationEndpointOptions> optionsAccessor,
        ITeamsNotificationTargetStore targetStore,
        IReportNotificationDeliveryStore deliveryStore,
        IReportNotificationCardFactory cardFactory,
        ITeamsProactiveNotificationSender sender,
        ILoggerFactory loggerFactory)
    {
        var options = optionsAccessor.Value;
        if (!options.Enabled) return Results.NotFound();
        if (!HasValidApiKey(httpContext, options.ApiKey))
            return Results.Unauthorized();

        InternalReportStatusNotificationRequest? notification;
        try
        {
            notification = await httpContext.Request
                .ReadFromJsonAsync<InternalReportStatusNotificationRequest>(
                    cancellationToken: httpContext.RequestAborted);
        }
        catch (JsonException) { return Results.BadRequest(); }
        catch (BadHttpRequestException) { return Results.BadRequest(); }
        if (!IsValid(notification)) return Results.BadRequest();

        var cardNotification = new ReportStatusNotificationRequest
        {
            RequestId = notification!.RequestId,
            Status = notification.Status,
            UpdatedAt = notification.UpdatedAt,
            CorrelationId = notification.CorrelationId,
            Summary = notification.Summary,
            PowerBiUrl = notification.PowerBiUrl,
            VisualizationPreview = notification.VisualizationPreview,
            ClarificationQuestion = notification.ClarificationQuestion,
            ErrorCode = notification.ErrorCode,
            RejectionMessage = notification.RejectionMessage
        };

        string conversationId;
        if (!string.IsNullOrWhiteSpace(notification.DeliveryId)
            && !string.IsNullOrWhiteSpace(notification.ConversationId))
        {
            conversationId = notification.ConversationId.Trim();
        }
        else
        {
            // Backward-compatible Development fallback. The durable backend
            // publisher always supplies the validated internal target.
            var target = await targetStore.GetByRequestIdAsync(
                notification.RequestId, httpContext.RequestAborted);
            if (target is null)
                return Results.Json(new
                {
                    errorCode = ReportNotificationHttpConstants
                        .TargetNotReadyErrorCode,
                    message = "Notification target is not ready."
                }, statusCode: StatusCodes.Status409Conflict);
            conversationId = target.ConversationId;
        }

        if (await deliveryStore.WasDeliveredAsync(cardNotification,
            httpContext.RequestAborted)) return Results.NoContent();

        var logger = loggerFactory.CreateLogger(
            typeof(ReportNotificationEndpointExtensions));
        try
        {
            await sender.SendCardAsync(conversationId,
                cardFactory.Create(cardNotification),
                httpContext.RequestAborted);
            await deliveryStore.MarkDeliveredAsync(cardNotification,
                httpContext.RequestAborted);
            logger.LogInformation(
                "Proactive report notification delivered for request {RequestId} with status {Status}.",
                notification.RequestId, notification.Status);
            return Results.NoContent();
        }
        catch (OperationCanceledException)
            when (httpContext.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            logger.LogError(
                "Proactive report notification delivery failed for request {RequestId} with status {Status}.",
                notification.RequestId, notification.Status);
            return Results.Json(new
            {
                errorCode = "NOTIFICATION_DELIVERY_FAILED",
                message = "Notification could not be delivered."
            }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static bool HasValidApiKey(HttpContext context, string key)
    {
        if (!context.Request.Headers.TryGetValue(
                ReportNotificationHttpConstants.ApiKeyHeaderName,
                out var supplied) || supplied.Count != 1) return false;
        return CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(supplied[0] ?? string.Empty)),
            SHA256.HashData(Encoding.UTF8.GetBytes(key)));
    }

    private static bool IsValid(InternalReportStatusNotificationRequest? value) =>
        value is not null
        && !string.IsNullOrWhiteSpace(value.RequestId)
        && !string.IsNullOrWhiteSpace(value.CorrelationId)
        && value.UpdatedAt.Offset == TimeSpan.Zero
        && Enum.IsDefined(value.Status)
        && IsValidPreview(value.VisualizationPreview)
        && ((value.DeliveryId is null && value.ConversationId is null)
            || (value.DeliveryId is not null
                && value.DeliveryId.Length == 64
                && value.DeliveryId.All(Uri.IsHexDigit)
                && !string.IsNullOrWhiteSpace(value.ConversationId)
                && value.ConversationId.Trim().Length <= 512));

    private static bool IsValidPreview(ReportVisualizationPreview? value)
    {
        if (value is null) return true;
        if (value.TotalRowCount < 0
            || value.DataPoints is null
            || value.DataPoints.Count > 8
            || string.IsNullOrWhiteSpace(value.Title)
            || value.Title.Length > 200
            || string.IsNullOrWhiteSpace(value.ValueLabel)
            || value.ValueLabel.Length > 100
            || value.CategoryLabel?.Length > 100)
            return false;

        if (value.Kind is not ReportVisualizationKinds.Kpi
            and not ReportVisualizationKinds.Bar
            and not ReportVisualizationKinds.Line
            and not ReportVisualizationKinds.None)
            return false;

        return value.DataPoints.All(point =>
            point is not null
            && point.Category is not null
            && point.Category.Length <= 100
            && double.IsFinite(point.Value));
    }
}
