namespace CrmAnalytics.Application.ReportProcessing;

public sealed class ReportProcessingQueueOptions
{
    public const string SectionName = "ReportProcessing:Queue";

    public bool Enabled { get; set; }

    public int Capacity { get; set; } = 100;
}
