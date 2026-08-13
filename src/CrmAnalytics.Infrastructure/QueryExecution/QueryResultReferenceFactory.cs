namespace CrmAnalytics.Infrastructure.QueryExecution;

public interface IQueryResultReferenceFactory
{
    string Create();
}

public sealed class QueryResultReferenceFactory
    : IQueryResultReferenceFactory
{
    public string Create() => $"qry_{Guid.NewGuid():N}";
}
