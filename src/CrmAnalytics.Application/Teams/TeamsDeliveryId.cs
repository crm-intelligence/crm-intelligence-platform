using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace CrmAnalytics.Application.Teams;

public static class TeamsDeliveryId
{
    public static string Create(
        string requestId,
        string status,
        DateTimeOffset reportUpdatedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        ArgumentException.ThrowIfNullOrWhiteSpace(status);
        if (reportUpdatedAt.Offset != TimeSpan.Zero)
            throw new ArgumentException("Timestamp must be UTC.",
                nameof(reportUpdatedAt));

        var input = string.Join("|", requestId.Trim(), status.Trim(),
            reportUpdatedAt.UtcTicks.ToString(CultureInfo.InvariantCulture));
        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(input)));
    }
}
