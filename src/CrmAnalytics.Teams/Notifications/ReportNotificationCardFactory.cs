using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CrmAnalytics.Contracts.Notifications;
using Microsoft.Teams.Cards;
using Microsoft.Teams.Common;
using AdaptiveCardVersion = Microsoft.Teams.Cards.Version;
using CardAction = Microsoft.Teams.Cards.Action;

namespace CrmAnalytics.Teams.Notifications;

public sealed class ReportNotificationCardFactory
    : IReportNotificationCardFactory
{
    internal const string AdaptiveCardSchema =
        "http://adaptivecards.io/schemas/adaptive-card.json";
    internal const string AdaptiveCardVersionValue = "1.5";

    private const int MaximumContentLength = 1000;
    private const string CompletedFallback =
        "Rapor başarıyla tamamlandı.";
    private const string ClarificationFallback =
        "Talebinizi tamamlayabilmemiz için ek bilgi paylaşmanız gerekiyor.";
    private const string FailedMessage =
        "Rapor talebi şu anda tamamlanamadı.";
    private const string RejectedFallback =
        "Rapor talebi güvenlik veya sorgu politikaları nedeniyle "
        + "işlenemedi.";

    public ReportNotificationCard Create(
        ReportStatusNotificationRequest notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            notification.RequestId);

        var requestId = notification.RequestId.Trim();
        var updatedAt = notification.UpdatedAt
            .ToUniversalTime()
            .ToString(
                "yyyy-MM-dd HH:mm 'UTC'",
                CultureInfo.InvariantCulture);

        return notification.Status switch
        {
            ReportNotificationStatus.Completed =>
                CreateCompleted(notification, requestId, updatedAt),
            ReportNotificationStatus.WaitingForClarification =>
                CreateWaitingForClarification(
                    notification,
                    requestId,
                    updatedAt),
            ReportNotificationStatus.Failed =>
                CreateFailed(requestId, updatedAt),
            ReportNotificationStatus.Rejected =>
                CreateRejected(notification, requestId, updatedAt),
            _ => throw new ArgumentOutOfRangeException(
                nameof(notification),
                "The notification status must be terminal.")
        };
    }

    private static ReportNotificationCard CreateCompleted(
        ReportStatusNotificationRequest notification,
        string requestId,
        string updatedAt)
    {
        ReportNotificationCard BuildCard()
        {
            var actions = new List<CardAction>();
            if (TryGetSafePowerBiUri(
                    notification.PowerBiUrl,
                    out var powerBiUri))
            {
                actions.Add(
                    new OpenUrlAction(powerBiUri.AbsoluteUri)
                    {
                        Title = "Power BI’da Aç"
                    });
            }

            actions.Add(CreateExecuteAction(
                title: "Revize Et",
                verb: "revise-report",
                requestId,
                notification.Status,
                notification.UpdatedAt));

            return CreateCard(
                title: "Raporunuz hazır",
                status: "Tamamlandı",
                description: NormalizeContent(
                    notification.Summary,
                    CompletedFallback),
                requestId,
                updatedAt,
                fallbackText:
                    "Raporunuz hazır. Durum: Tamamlandı.",
                actions,
                additionalBody:
                [
                    new TextInput
                    {
                        Id = "revisionPrompt",
                        IsMultiline = true,
                        Placeholder =
                            "Örneğin: Satış adedi yerine net kârı göster.",
                        MaxLength = 2000
                    }
                ]);
        }

        var textOnly = BuildCard();
        if (notification.VisualizationPreview is null)
            return textOnly;

        try
        {
            var visual = BuildCard();
            AddVisualizationElements(
                visual.Card,
                notification.VisualizationPreview);
            _ = JsonSerializer.Serialize(visual.Card);
            return new ReportNotificationCard(
                visual.Card, textOnly.Card);
        }
        catch (Exception exception) when (exception is not
            OperationCanceledException)
        {
            return textOnly;
        }
    }

    private static ReportNotificationCard
        CreateWaitingForClarification(
            ReportStatusNotificationRequest notification,
            string requestId,
            string updatedAt)
    {
        return CreateCard(
            title: "Ek bilgi gerekiyor",
            status: "Açıklama bekleniyor",
            description: NormalizeContent(
                notification.ClarificationQuestion,
                ClarificationFallback),
            requestId,
            updatedAt,
            fallbackText:
                "Ek bilgi gerekiyor. Durum: Açıklama bekleniyor.",
            actions:
            [
                CreateExecuteAction(
                    title: "Açıklamayı Gönder",
                    verb: "submit-clarification",
                    requestId,
                    notification.Status,
                    notification.UpdatedAt)
            ],
            additionalBody:
            [
                new TextInput
                {
                    Id = "clarificationResponse",
                    IsMultiline = true,
                    Placeholder =
                        "İstenen ek bilgiyi buraya yazın.",
                    MaxLength = 2000
                }
            ]);
    }

    private static ReportNotificationCard CreateFailed(
        string requestId,
        string updatedAt)
    {
        return CreateCard(
            title: "Rapor tamamlanamadı",
            status: "Başarısız",
            description: FailedMessage,
            requestId,
            updatedAt,
            fallbackText:
                "Rapor tamamlanamadı. Durum: Başarısız.",
            actions: [],
            additionalBody: []);
    }

    private static ReportNotificationCard CreateRejected(
        ReportStatusNotificationRequest notification,
        string requestId,
        string updatedAt)
    {
        return CreateCard(
            title: "Rapor talebi işlenemedi",
            status: "Reddedildi",
            description: NormalizeContent(
                notification.RejectionMessage,
                RejectedFallback),
            requestId,
            updatedAt,
            fallbackText:
                "Rapor talebi işlenemedi. Durum: Reddedildi.",
            actions: [],
            additionalBody: []);
    }

    private static ReportNotificationCard CreateCard(
        string title,
        string status,
        string description,
        string requestId,
        string updatedAt,
        string fallbackText,
        IList<CardAction> actions,
        IList<CardElement> additionalBody)
    {
        var card = new AdaptiveCard(
            new TextBlock(title)
            {
                Size = new TextSize("Medium"),
                Weight = new TextWeight("Bolder"),
                Wrap = true
            },
            new TextBlock($"Durum: {status}")
            {
                Weight = new TextWeight("Bolder"),
                Wrap = true
            },
            new TextBlock(description)
            {
                Wrap = true
            },
            new FactSet(
                new Fact("İşlem numarası", requestId),
                new Fact("Güncellenme zamanı", updatedAt)))
        {
            Schema = AdaptiveCardSchema,
            Version = new AdaptiveCardVersion(
                AdaptiveCardVersionValue),
            FallbackText = fallbackText,
            Actions = actions
        };
        foreach (var element in additionalBody)
        {
            card.Body!.Add(element);
        }

        return new ReportNotificationCard(card);
    }

    private static void AddVisualizationElements(
        AdaptiveCard card,
        ReportVisualizationPreview preview)
    {
        if (preview.DataPoints.Count == 0) return;

        var elements = new List<CardElement>
        {
            new TextBlock(preview.Title)
            {
                Weight = new TextWeight("Bolder"),
                Wrap = true,
                Separator = true
            }
        };

        switch (preview.Kind)
        {
            case ReportVisualizationKinds.Kpi:
                elements.Add(new TextBlock(
                    preview.DataPoints[0].Value.ToString(
                        "N0", CultureInfo.GetCultureInfo("tr-TR")))
                {
                    Size = new TextSize("ExtraLarge"),
                    Weight = new TextWeight("Bolder"),
                    Wrap = true
                });
                break;
            case ReportVisualizationKinds.Bar:
                elements.Add(new HorizontalBarChart
                {
                    Title = preview.Title,
                    XAxisTitle = preview.ValueLabel,
                    YAxisTitle = preview.CategoryLabel,
                    Data = preview.DataPoints.Select(point =>
                        new HorizontalBarChartDataValue
                        {
                            X = point.Category,
                            Y = (float)point.Value
                        }).ToList()
                });
                break;
            case ReportVisualizationKinds.Line:
                elements.Add(new LineChart
                {
                    Title = preview.Title,
                    XAxisTitle = preview.CategoryLabel,
                    YAxisTitle = preview.ValueLabel,
                    Data =
                    [
                        new LineChartData
                        {
                            Legend = preview.ValueLabel,
                            Values = preview.DataPoints.Select(point =>
                                new LineChartValue
                                {
                                    X = new Union<float, string>(
                                        point.Category),
                                    Y = (float)point.Value
                                }).ToList()
                        }
                    ]
                });
                break;
            default:
                return;
        }

        if (preview.Kind is ReportVisualizationKinds.Bar
            or ReportVisualizationKinds.Line)
        {
            var suffix = preview.TotalRowCount > preview.DataPoints.Count
                ? $" Grafikte ilk {preview.DataPoints.Count} gösteriliyor."
                : string.Empty;
            elements.Add(new TextBlock(
                $"Toplam sonuç: {preview.TotalRowCount}.{suffix}")
            {
                IsSubtle = true,
                Wrap = true
            });
        }

        var body = card.Body!;
        var insertionIndex = 0;
        while (insertionIndex < body.Count
            && body[insertionIndex] is not TextInput)
            insertionIndex++;
        foreach (var element in elements)
            body.Insert(insertionIndex++, element);
    }

    private static ExecuteAction CreateExecuteAction(
        string title,
        string verb,
        string requestId,
        ReportNotificationStatus status,
        DateTimeOffset updatedAt)
    {
        var data = new SubmitActionData
        {
            NonSchemaProperties = new Dictionary<string, object?>
            {
                ["requestId"] = requestId,
                ["actionToken"] = CreateActionToken(
                    requestId,
                    status,
                    updatedAt,
                    verb)
            }
        };

        return new ExecuteAction
        {
            Title = title,
            Verb = verb,
            AssociatedInputs = AssociatedInputs.Auto,
            Data = new Union<string, SubmitActionData>(data)
        };
    }

    private static string CreateActionToken(
        string requestId,
        ReportNotificationStatus status,
        DateTimeOffset updatedAt,
        string verb)
    {
        var value = string.Join(
            "\n",
            requestId,
            status.ToString(),
            updatedAt.ToUniversalTime().Ticks
                .ToString(CultureInfo.InvariantCulture),
            verb);
        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();
    }

    private static string NormalizeContent(
        string? value,
        string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= MaximumContentLength
            ? trimmed
            : trimmed[..MaximumContentLength];
    }

    private static bool TryGetSafePowerBiUri(
        string? value,
        out Uri uri)
    {
        if (Uri.TryCreate(
                value?.Trim(),
                UriKind.Absolute,
                out var candidate)
            && string.Equals(
                candidate.Scheme,
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                candidate.Host,
                "app.powerbi.com",
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
}
