namespace CrmAnalytics.Infrastructure.Integrations;

public sealed class MockExternalServicesOptions
{
    public const string SectionName = "ExternalServices:Mocks";

    public int DelayMilliseconds { get; set; } = 50;

    public string PowerBiBaseUrl { get; set; } =
        "https://app.powerbi.com/demo/reports";

    public bool ForceQueryPlanningFailure { get; set; }

    public bool ForceAnalyticsFailure { get; set; }

    public bool ForceReportFailure { get; set; }

    public bool ForceClarification { get; set; }
}
