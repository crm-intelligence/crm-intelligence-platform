namespace CrmAnalytics.Application.Exceptions;

public sealed class ApplicationAuditPersistenceException : Exception
{
    public ApplicationAuditPersistenceException()
        : base("The application audit event could not be persisted.")
    {
    }
}
