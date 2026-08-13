namespace CrmAnalytics.Contracts.Notifications;

public static class ReportNotificationHttpConstants
{
    public const string ApiKeyHeaderName =
        "X-CrmAnalytics-Notification-Key";

    public const string CallbackPath =
        "/api/internal/report-notifications";

    public const string TargetNotReadyErrorCode =
        "NOTIFICATION_TARGET_NOT_READY";
}
