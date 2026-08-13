using System.Text.Json;
using CrmAnalytics.Contracts.Notifications;
using CrmAnalytics.Teams.Notifications;
using Microsoft.Teams.Cards;

namespace CrmAnalytics.UnitTests;

public sealed class ReportNotificationCardFactoryTests
{
    private static readonly DateTimeOffset UpdatedAt =
        new(2026, 7, 30, 10, 45, 0, TimeSpan.Zero);

    private readonly ReportNotificationCardFactory _factory = new();

    [Fact]
    public void Completed_CreatesExpectedAdaptiveCard()
    {
        var card = _factory.Create(
            CreateNotification(
                ReportNotificationStatus.Completed,
                summary: "  Satış özeti  "));

        Assert.Equal("AdaptiveCard", card.Card.Type);
        var content = GetVisibleContent(card);
        Assert.Contains("Raporunuz hazır", content);
        Assert.Contains("Durum: Tamamlandı", content);
        Assert.Contains("Satış özeti", content);
        Assert.Contains("İşlem numarası: request-1", content);
    }

    [Fact]
    public void Completed_EmptySummaryUsesFallback()
    {
        var card = _factory.Create(
            CreateNotification(ReportNotificationStatus.Completed));

        Assert.Contains(
            "Rapor başarıyla tamamlandı.",
            GetVisibleContent(card));
    }

    [Fact]
    public void Completed_SummaryIsLimitedTo1000Characters()
    {
        var card = _factory.Create(
            CreateNotification(
                ReportNotificationStatus.Completed,
                summary: new string('x', 1200)));

        var summary = Assert.Single(
            card.Card.Body!.OfType<TextBlock>(),
            block => block.Text?.StartsWith('x') == true);
        Assert.Equal(1000, summary.Text!.Length);
    }

    [Fact]
    public void UpdatedAt_IsDisplayedInUtcFormat()
    {
        var notification = CreateNotification(
            ReportNotificationStatus.Completed) with
        {
            UpdatedAt = new DateTimeOffset(
                2026,
                7,
                30,
                13,
                45,
                0,
                TimeSpan.FromHours(3))
        };

        var card = _factory.Create(notification);

        Assert.Contains(
            "Güncellenme zamanı: 2026-07-30 10:45 UTC",
            GetVisibleContent(card));
    }

    [Fact]
    public void Completed_HttpsPowerBiUrlCreatesOneOpenUrlAction()
    {
        var card = _factory.Create(
            CreateNotification(
                ReportNotificationStatus.Completed,
                powerBiUrl:
                    " https://app.powerbi.com/report/42 "));

        var action = Assert.Single(
            card.Card.Actions!.OfType<OpenUrlAction>());
        Assert.Equal("Power BI’da Aç", action.Title);
        Assert.Equal(
            "https://app.powerbi.com/report/42",
            action.Url);
        Assert.DoesNotContain(
            action.Url!,
            GetVisibleContent(card));
    }

    [Theory]
    [InlineData("http://app.powerbi.com/report/42")]
    [InlineData("/reports/42")]
    [InlineData("not-a-url")]
    [InlineData("https:///reports/42")]
    public void Completed_UnsafePowerBiUrlDoesNotCreateAction(
        string url)
    {
        var card = _factory.Create(
            CreateNotification(
                ReportNotificationStatus.Completed,
                powerBiUrl: url));

        Assert.Empty(card.Card.Actions!.OfType<OpenUrlAction>());
        Assert.Single(card.Card.Actions!.OfType<ExecuteAction>());
    }

    [Fact]
    public void Completed_NullPowerBiUrlStillCreatesValidCard()
    {
        var card = _factory.Create(
            CreateNotification(ReportNotificationStatus.Completed));

        Assert.Single(card.Card.Actions!.OfType<ExecuteAction>());
        AssertValidSerialization(card);
    }

    [Fact]
    public void Completed_KpiPreviewRendersLargeValueAndKeepsRevision()
    {
        var card = _factory.Create(
            CreateNotification(ReportNotificationStatus.Completed) with
            {
                VisualizationPreview = Preview(
                    ReportVisualizationKinds.Kpi,
                    [new() { Category = "", Value = 8423 }],
                    totalRowCount: 1)
            });

        Assert.Contains(card.Card.Body!.OfType<TextBlock>(), block =>
            block.Text?.Contains("8.423", StringComparison.Ordinal) == true);
        Assert.Contains(card.Card.Actions!.OfType<ExecuteAction>(), action =>
            action.Verb == "revise-report");
    }

    [Theory]
    [InlineData(ReportVisualizationKinds.Bar, "Chart.HorizontalBar")]
    [InlineData(ReportVisualizationKinds.Line, "Chart.Line")]
    public void Completed_ChartPreviewSerializesNativeTeamsElement(
        string kind,
        string expectedType)
    {
        var card = _factory.Create(
            CreateNotification(ReportNotificationStatus.Completed) with
            {
                VisualizationPreview = Preview(
                    kind,
                    [
                        new() { Category = "A", Value = 9 },
                        new() { Category = "B", Value = 4 }
                    ],
                    totalRowCount: 66)
            });

        using var document = JsonDocument.Parse(
            JsonSerializer.Serialize(card.Card));
        Assert.Contains(document.RootElement.GetProperty("body")
            .EnumerateArray(), element =>
                element.GetProperty("type").GetString() == expectedType);
        Assert.NotNull(card.FallbackCard);
        Assert.Contains(card.Card.Actions!.OfType<ExecuteAction>(), action =>
            action.Verb == "revise-report");
    }

    [Fact]
    public void WaitingForClarification_CreatesExpectedCard()
    {
        var card = _factory.Create(
            CreateNotification(
                ReportNotificationStatus.WaitingForClarification,
                clarificationQuestion: "  Hangi dönem?  "));

        var content = GetVisibleContent(card);
        Assert.Contains("Ek bilgi gerekiyor", content);
        Assert.Contains("Durum: Açıklama bekleniyor", content);
        Assert.Contains("Hangi dönem?", content);
        Assert.Contains("İşlem numarası: request-1", content);
        Assert.Single(card.Card.Actions!.OfType<ExecuteAction>());
    }

    [Fact]
    public void WaitingForClarification_EmptyQuestionUsesFallback()
    {
        var card = _factory.Create(
            CreateNotification(
                ReportNotificationStatus.WaitingForClarification));

        Assert.Contains(
            "Talebinizi tamamlayabilmemiz için ek bilgi "
                + "paylaşmanız gerekiyor.",
            GetVisibleContent(card));
    }

    [Fact]
    public void WaitingForClarification_QuestionIsLimitedTo1000Characters()
    {
        var card = _factory.Create(
            CreateNotification(
                ReportNotificationStatus.WaitingForClarification,
                clarificationQuestion: new string('q', 1200)));

        var question = Assert.Single(
            card.Card.Body!.OfType<TextBlock>(),
            block => block.Text?.StartsWith('q') == true);
        Assert.Equal(1000, question.Text!.Length);
    }

    [Fact]
    public void Failed_UsesSafeMessageAndOmitsTechnicalFields()
    {
        var notification = CreateNotification(
            ReportNotificationStatus.Failed,
            errorCode: "SECRET_TECHNICAL_FAILURE");

        var card = _factory.Create(notification);
        var serialized = JsonSerializer.Serialize(card.Card);

        Assert.Contains(
            "Rapor talebi şu anda tamamlanamadı.",
            GetVisibleContent(card));
        Assert.Contains("Durum: Başarısız", GetVisibleContent(card));
        Assert.DoesNotContain(
            notification.ErrorCode!,
            serialized,
            StringComparison.Ordinal);
        Assert.Empty(card.Card.Actions!);
    }

    [Fact]
    public void Rejected_UsesSafeMessageAndOmitsCodesAndActions()
    {
        var notification = CreateNotification(
            ReportNotificationStatus.Rejected,
            errorCode: "MUST_NOT_APPEAR",
            rejectionMessage: "Bu veri kapsamı desteklenmiyor.");

        var card = _factory.Create(notification);
        var serialized = JsonSerializer.Serialize(card.Card);
        var content = GetVisibleContent(card);

        Assert.Contains("Rapor talebi işlenemedi", content);
        Assert.Contains("Durum: Reddedildi", content);
        Assert.Contains("Bu veri kapsamı desteklenmiyor.", content);
        Assert.DoesNotContain("MUST_NOT_APPEAR", serialized);
        Assert.DoesNotContain("GR007", serialized);
        Assert.DoesNotContain("SELECT", serialized);
        Assert.Empty(card.Card.Actions!);
    }

    [Theory]
    [InlineData(ReportNotificationStatus.Completed)]
    [InlineData(ReportNotificationStatus.Failed)]
    [InlineData(ReportNotificationStatus.WaitingForClarification)]
    [InlineData(ReportNotificationStatus.Rejected)]
    public void Cards_DoNotIncludeUnrelatedSensitiveFields(
        ReportNotificationStatus status)
    {
        var notification = CreateNotification(
            status,
            summary: "Kullanıcı özeti",
            clarificationQuestion: "Kullanıcı sorusu",
            errorCode: "internal-error-code");

        var serialized = JsonSerializer.Serialize(
            _factory.Create(notification).Card);

        Assert.DoesNotContain(
            notification.CorrelationId,
            serialized,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            notification.ErrorCode!,
            serialized,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "conversation-id",
            serialized,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "secret-prompt-value",
            serialized,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SpecialCharactersAndNewLines_SerializeAsText()
    {
        const string summary =
            "Satış \"özeti\"\n[bağlantı](https://untrusted.example)";
        var card = _factory.Create(
            CreateNotification(
                ReportNotificationStatus.Completed,
                summary: summary,
                powerBiUrl:
                    "https://app.powerbi.com/report/42"));

        using var document = JsonDocument.Parse(
            JsonSerializer.Serialize(card.Card));
        var summaryElement = document.RootElement
            .GetProperty("body")
            .EnumerateArray()
            .Single(element =>
                element.GetProperty("type").GetString()
                    == "TextBlock"
                && element.GetProperty("text").GetString()
                    == summary);

        Assert.Equal(summary, summaryElement.GetProperty("text").GetString());
        Assert.Equal(2, card.Card.Actions!.Count);
        Assert.Equal(
            "https://app.powerbi.com/report/42",
            Assert.IsType<OpenUrlAction>(
                card.Card.Actions![0]).Url);
    }

    [Fact]
    public void SchemaAndVersion_AreConservativeTeamsValues()
    {
        var card = _factory.Create(
            CreateNotification(ReportNotificationStatus.Completed));

        using var document = JsonDocument.Parse(
            JsonSerializer.Serialize(card.Card));
        Assert.Equal(
            ReportNotificationCardFactory.AdaptiveCardSchema,
            document.RootElement.GetProperty("$schema").GetString());
        Assert.Equal(
            ReportNotificationCardFactory.AdaptiveCardVersionValue,
            document.RootElement.GetProperty("version").GetString());
    }

    [Theory]
    [InlineData(ReportNotificationStatus.Completed)]
    [InlineData(ReportNotificationStatus.Failed)]
    [InlineData(ReportNotificationStatus.WaitingForClarification)]
    [InlineData(ReportNotificationStatus.Rejected)]
    public void FallbackText_IsSafe(
        ReportNotificationStatus status)
    {
        var notification = CreateNotification(
            status,
            summary: "secret summary",
            clarificationQuestion: "secret question",
            errorCode: "secret error");

        var card = _factory.Create(notification);

        Assert.False(string.IsNullOrWhiteSpace(card.Card.FallbackText));
        Assert.DoesNotContain(
            "secret",
            card.Card.FallbackText!,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            notification.CorrelationId,
            card.Card.FallbackText!,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "conversation",
            card.Card.FallbackText!,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "prompt",
            card.Card.FallbackText!,
            StringComparison.OrdinalIgnoreCase);
    }

    private static ReportStatusNotificationRequest CreateNotification(
        ReportNotificationStatus status,
        string? summary = null,
        string? powerBiUrl = null,
        string? clarificationQuestion = null,
        string? errorCode = null,
        string? rejectionMessage = null) =>
        new()
        {
            RequestId = "request-1",
            Status = status,
            UpdatedAt = UpdatedAt,
            CorrelationId = "correlation-should-not-be-rendered",
            Summary = summary,
            PowerBiUrl = powerBiUrl,
            ClarificationQuestion = clarificationQuestion,
            ErrorCode = errorCode,
            RejectionMessage = rejectionMessage
        };

    private static ReportVisualizationPreview Preview(
        string kind,
        IReadOnlyList<ReportVisualizationDataPoint> points,
        int totalRowCount) => new()
        {
            Kind = kind,
            Title = "Ürün kategorisine göre Sipariş Sayısı",
            CategoryLabel = "Ürün Kategorisi",
            ValueLabel = "Sipariş Sayısı",
            DataPoints = points,
            TotalRowCount = totalRowCount
        };

    private static string GetVisibleContent(
        ReportNotificationCard card)
    {
        var text = card.Card.Body!
            .OfType<TextBlock>()
            .Select(block => block.Text ?? string.Empty);
        var facts = card.Card.Body!
            .OfType<FactSet>()
            .SelectMany(factSet => factSet.Facts ?? [])
            .Select(fact => $"{fact.Title}: {fact.Value}");

        return string.Join(Environment.NewLine, text.Concat(facts));
    }

    private static void AssertValidSerialization(
        ReportNotificationCard card)
    {
        using var document = JsonDocument.Parse(
            JsonSerializer.Serialize(card.Card));
        Assert.Equal(
            "AdaptiveCard",
            document.RootElement.GetProperty("type").GetString());
    }
}
