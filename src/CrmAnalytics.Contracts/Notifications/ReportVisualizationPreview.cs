namespace CrmAnalytics.Contracts.Notifications;

public static class ReportVisualizationKinds
{
    public const string None = "none";
    public const string Kpi = "kpi";
    public const string Bar = "bar";
    public const string Line = "line";
}

public sealed record ReportVisualizationPreview
{
    public required string Kind { get; init; }
    public required string Title { get; init; }
    public string? CategoryLabel { get; init; }
    public required string ValueLabel { get; init; }
    public required IReadOnlyList<ReportVisualizationDataPoint> DataPoints
    {
        get;
        init;
    }
    public required int TotalRowCount { get; init; }
}

public sealed record ReportVisualizationDataPoint
{
    public required string Category { get; init; }
    public required double Value { get; init; }
}
