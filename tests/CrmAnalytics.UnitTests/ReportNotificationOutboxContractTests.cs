using CrmAnalytics.Application.Outbox;
using CrmAnalytics.Contracts.Notifications;

namespace CrmAnalytics.UnitTests;

public sealed class ReportNotificationOutboxContractTests
{
    [Fact]
    public void Envelope_IsVersionedMinimalAndRoundTrips()
    {
        var now = new DateTimeOffset(2026, 7, 31, 12, 0, 0,
            TimeSpan.Zero);
        var serializer = new OutboxMessageSerializer();
        var factory = new OutboxMessageFactory(serializer);
        var message = factory.CreateReportNotificationRequested(
            "request-1", ReportNotificationStatus.Completed, now);
        var envelope = serializer.DeserializeReportNotificationRequested(
            message.PayloadJson);

        Assert.Equal(OutboxMessageType.ReportNotificationRequested,
            message.MessageType);
        Assert.Equal(2, envelope.SchemaVersion);
        Assert.Equal("request-1", envelope.RequestId);
        Assert.Equal(ReportNotificationStatus.Completed, envelope.Status);
        foreach (var forbidden in new[]
        {
            "conversation", "prompt", "sql", "canonical", "user",
            "tenant", "scope", "token", "summary", "powerBi"
        })
            Assert.DoesNotContain(forbidden, message.PayloadJson,
                StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CompletedPreview_RoundTripsAtMaximumEightPoints()
    {
        var serializer = new OutboxMessageSerializer();
        var factory = new OutboxMessageFactory(serializer);
        var preview = new ReportVisualizationPreview
        {
            Kind = ReportVisualizationKinds.Bar,
            Title = "Kategori bazında sipariş",
            CategoryLabel = "Kategori",
            ValueLabel = "Sipariş Sayısı",
            DataPoints = Enumerable.Range(1, 8).Select(index =>
                new ReportVisualizationDataPoint
                {
                    Category = $"category-{index}",
                    Value = index
                }).ToArray(),
            TotalRowCount = 66
        };

        var message = factory.CreateReportNotificationRequested(
            "request-1", ReportNotificationStatus.Completed,
            DateTimeOffset.UtcNow, preview);
        var envelope = serializer.DeserializeReportNotificationRequested(
            message.PayloadJson);

        Assert.Equal(8, envelope.VisualizationPreview!.DataPoints.Count);
        Assert.Equal(66, envelope.VisualizationPreview.TotalRowCount);
        Assert.DoesNotContain("SELECT", message.PayloadJson,
            StringComparison.OrdinalIgnoreCase);
    }
}
