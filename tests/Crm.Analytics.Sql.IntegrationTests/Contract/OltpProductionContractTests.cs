using Crm.Analytics.Sql.Audit;
using Crm.Analytics.Sql.Contracts;
using Crm.Analytics.Sql.Guardrail;
using Crm.Analytics.Sql.Service;

namespace Crm.Analytics.Sql.IntegrationTests.Contract;

public sealed class OltpProductionContractTests
{
    [Fact]
    public void Automatic_source_selection_runs_real_Oltp_builder_and_guardrail()
    {
        var response = Service().Produce(Request("Bugunku siparisleri getir"));

        Assert.Equal(GuardrailDecision.Accepted, response.Decision);
        Assert.Equal(DataSource.Oltp, response.Source);
        Assert.Equal(15, response.CommandTimeoutSeconds);
        Assert.Contains("TOP 100", response.Sql!, StringComparison.Ordinal);
        Assert.Contains("FROM dbo.vw_operational_orders", response.Sql!, StringComparison.Ordinal);
        Assert.Contains("@f0", response.Sql!, StringComparison.Ordinal);
        Assert.DoesNotContain("mart.", response.Sql!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Source_ambiguity_stops_before_SQL_generation()
    {
        var response = Service().Produce(Request("Bugunku siparis durumu"));
        Assert.Equal(GuardrailDecision.NeedsClarification, response.Decision);
        Assert.Null(response.Sql);
        Assert.Empty(response.Checks);
    }

    private static ISqlProductionService Service() =>
        SqlProductionFactory.CreateForOlist(new NullAuditWriter());

    private static SqlProductionRequest Request(string prompt) => new()
    {
        RequestId = Guid.NewGuid().ToString("N"),
        ConversationId = "integration-conversation",
        Prompt = prompt,
        Scope = UserDataScope.Unrestricted,
        Today = new DateOnly(2026, 8, 5)
    };

    private sealed class NullAuditWriter : IDecisionAuditWriter
    {
        public void Write(DecisionAuditRecord record)
        {
        }
    }
}
