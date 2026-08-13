namespace CrmAnalytics.Application.Exceptions;

public sealed class ForbiddenAccessException : Exception
{
    public const string ErrorCode = "ACCESS_DENIED";

    public const string SafeMessage =
        "Bu işlem için yetkiniz bulunmuyor.";

    public ForbiddenAccessException()
        : base(SafeMessage)
    {
    }
}
