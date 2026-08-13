using Crm.Analytics.Sql.Audit;
using Microsoft.Extensions.Logging;

namespace CrmAnalytics.Infrastructure.Integrations;

internal sealed class SafeSqlDecisionAuditWriter(
    ILogger<SafeSqlDecisionAuditWriter> logger)
    : IDecisionAuditWriter
{
    public void Write(DecisionAuditRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var failedCheck = record.Checks
            .FirstOrDefault(check => check.Outcome
                == Crm.Analytics.Sql.Contracts.CheckOutcome.Failed);

        logger.Log(
            record.Decision == Crm.Analytics.Sql.Contracts.GuardrailDecision.Rejected
                ? LogLevel.Warning
                : LogLevel.Information,
            "SQL production decision {Decision}; reason {ReasonCode}; failed check {FailedCheck}; verified checks {VerifiedCheckCount}; source {Source}.",
            record.Decision,
            record.ReasonCode,
            failedCheck?.Name,
            record.VerifiedCheckCount,
            record.Source);
    }
}
