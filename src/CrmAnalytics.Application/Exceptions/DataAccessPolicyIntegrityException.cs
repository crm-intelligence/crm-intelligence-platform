namespace CrmAnalytics.Application.Exceptions;

public sealed class DataAccessPolicyIntegrityException : Exception
{
    public const string ErrorCode = "DATA_ACCESS_POLICY_ERROR";
    public const string SafeMessage =
        "Veri erişim politikası doğrulanamadı. Lütfen sistem "
        + "yöneticinizle iletişime geçin.";

    public DataAccessPolicyIntegrityException()
        : base(SafeMessage)
    {
    }
}
