namespace CrmAnalytics.Contracts.Integrations;

public sealed record AnalyticsRequest
{
    private IReadOnlyDictionary<string, string?> _parameters =
        new Dictionary<string, string?>();

    public AnalyticsRequest(
        string RequestId,
        string AnalysisType,
        string QueryResultReference,
        IReadOnlyDictionary<string, string?> Parameters)
    {
        this.RequestId = RequestId;
        this.AnalysisType = AnalysisType;
        this.QueryResultReference = QueryResultReference;
        this.Parameters = Parameters;
    }

    public string RequestId { get; init; }

    public string AnalysisType { get; init; }

    public string QueryResultReference { get; init; }

    public IReadOnlyDictionary<string, string?> Parameters
    {
        get => _parameters;
        init => _parameters =
            value ?? new Dictionary<string, string?>();
    }
}
