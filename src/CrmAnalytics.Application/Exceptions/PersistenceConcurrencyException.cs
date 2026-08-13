namespace CrmAnalytics.Application.Exceptions;

public sealed class PersistenceConcurrencyException : Exception
{
    public const string ErrorCode = "CONCURRENCY_CONFLICT";

    public const string SafeMessage =
        "Kayıt başka bir işlem tarafından güncellendi. "
        + "Lütfen işlemi yeniden deneyin.";

    public PersistenceConcurrencyException()
        : base(SafeMessage)
    {
    }

    public PersistenceConcurrencyException(Exception innerException)
        : base(SafeMessage, innerException)
    {
    }
}
