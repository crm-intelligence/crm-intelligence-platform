namespace CrmAnalytics.Infrastructure.Integrations;

public sealed class ReportingOptions
{
    public const string SectionName = "Reporting";

    public string Provider { get; set; } = ReportingProviders.Mock;
    public PowerBiOptions PowerBi { get; set; } = new();
}

public static class ReportingProviders
{
    public const string Mock = "Mock";
    public const string PowerBi = "PowerBi";
}

public sealed class PowerBiOptions
{
    public const string DefaultOltpReportId =
        "f7e7794b-a2f9-4330-afe3-c79946e36461";
    public const string DefaultSalesAnalysisPageId =
        "033e576a7c43418160b7";
    public const string DefaultCustomerSegmentationPageId =
        "84686876a120a0b04881";
    public const string DefaultPaymentAnalysisPageId =
        "637aeb7338de115d4c9c";
    public const string DefaultOltpPageId =
        "3e8e9e450843cce5a86a";

    public string WorkspaceId { get; set; } = string.Empty;
    public string ReportId { get; set; } = string.Empty;
    public string OltpReportId { get; set; } = DefaultOltpReportId;
    public string SalesAnalysisPageId { get; set; } =
        DefaultSalesAnalysisPageId;
    public string CustomerSegmentationPageId { get; set; } =
        DefaultCustomerSegmentationPageId;
    public string PaymentAnalysisPageId { get; set; } =
        DefaultPaymentAnalysisPageId;
    public string OltpPageId { get; set; } = DefaultOltpPageId;
    public string SemanticModelId { get; set; } = string.Empty;
    public bool RefreshBeforeReturn { get; set; }
    public bool RefreshPollingEnabled { get; set; }
    public int RefreshPollingIntervalSeconds { get; set; } = 5;
    public int RefreshTimeoutSeconds { get; set; } = 600;
    public int MaxRetries { get; set; } = 4;
    public string AuthenticationMode { get; set; } = "ManagedIdentity";
    public string ManagedIdentityClientId { get; set; } = string.Empty;
}
