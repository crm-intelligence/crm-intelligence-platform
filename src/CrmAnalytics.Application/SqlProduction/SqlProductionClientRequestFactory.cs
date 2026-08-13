using CrmAnalytics.Contracts.Integrations;
using CrmAnalytics.Domain.ReportRequests;

namespace CrmAnalytics.Application.SqlProduction;

public static class SqlProductionClientRequestFactory
{
    public static SqlProductionClientRequest Create(
        ReportRequest reportRequest,
        ReportRequest? previousReportRequest,
        UserDataScope userDataScope,
        SqlDataSource source = SqlDataSource.Unknown,
        SubmittedSemanticPlanningResult? semanticPlan = null)
    {
        ArgumentNullException.ThrowIfNull(reportRequest);
        ArgumentNullException.ThrowIfNull(userDataScope);

        var isClarificationResume =
            reportRequest.ClarificationResponse is not null;

        string prompt;
        string? previousCanonicalRequestJson;

        if (isClarificationResume)
        {
            if (string.IsNullOrWhiteSpace(
                    reportRequest.ClarificationResponse))
            {
                throw new InvalidOperationException(
                    "A clarification response is required to resume SQL "
                        + "production.");
            }

            if (string.IsNullOrWhiteSpace(
                    reportRequest.CanonicalRequestJson))
            {
                // A parser-level clarification can be requested before a
                // canonical request exists. Reparse the original request with
                // the single clarification answer as one full request.
                prompt = string.Concat(
                    reportRequest.Prompt.Trim(),
                    Environment.NewLine,
                    reportRequest.ClarificationResponse.Trim());
                previousCanonicalRequestJson = null;
            }
            else
            {
                prompt = reportRequest.ClarificationResponse;
                previousCanonicalRequestJson =
                    reportRequest.CanonicalRequestJson;
            }
        }
        else
        {
            prompt = reportRequest.Prompt;
            previousCanonicalRequestJson =
                CanUsePreviousContext(
                    reportRequest,
                    previousReportRequest)
                    ? previousReportRequest!.CanonicalRequestJson
                    : null;
        }

        return new SqlProductionClientRequest(
            RequestId: reportRequest.RequestId,
            ConversationId: reportRequest.ConversationId,
            Prompt: prompt,
            Today: reportRequest.ReferenceDate,
            UserDataScope: userDataScope,
            PreviousCanonicalRequestJson:
                previousCanonicalRequestJson,
            UserId: reportRequest.UserId,
            Source: source,
            OriginalPrompt: isClarificationResume
                ? reportRequest.Prompt
                : null,
            ClarificationQuestion: isClarificationResume
                ? reportRequest.ClarificationQuestion
                : null,
            ClarificationAnswer: isClarificationResume
                ? reportRequest.ClarificationResponse
                : null,
            SemanticPlan: semanticPlan);
    }

    private static bool CanUsePreviousContext(
        ReportRequest reportRequest,
        ReportRequest? previousReportRequest)
    {
        return reportRequest.PreviousRequestId is not null
            && previousReportRequest is not null
            && string.Equals(
                reportRequest.PreviousRequestId,
                previousReportRequest.RequestId,
                StringComparison.Ordinal)
            && IdentifiersEqual(
                reportRequest.UserId,
                previousReportRequest.UserId)
            && IdentifiersEqual(
                reportRequest.TenantId,
                previousReportRequest.TenantId);
    }

    private static bool IdentifiersEqual(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left)
            || string.IsNullOrWhiteSpace(right))
        {
            return left is null && right is null;
        }

        return Guid.TryParse(left.Trim(), out var leftGuid)
            && Guid.TryParse(right.Trim(), out var rightGuid)
            ? leftGuid == rightGuid
            : string.Equals(
                left.Trim(),
                right.Trim(),
                StringComparison.Ordinal);
    }
}
