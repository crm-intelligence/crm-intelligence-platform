using CrmAnalytics.Application.SqlProduction;
using CrmAnalytics.Infrastructure.Integrations;

namespace CrmAnalytics.UnitTests;

public sealed class SqlProductionPreparationTests
{
    [Fact]
    public async Task UnconfiguredExecutor_FailsClosedWithoutLeakingPlan()
    {
        var plan = CreatePlan();
        var client = new UnconfiguredQueryExecutionClient();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.ExecuteAsync(plan, CancellationToken.None));

        Assert.Equal(
            "Gerçek sorgu çalıştırma servisi yapılandırılmamış.",
            exception.Message);
        Assert.DoesNotContain("SELECT", exception.ToString());
        Assert.DoesNotContain("secret", exception.ToString());
    }

    [Fact]
    public async Task UnconfiguredExecutor_PropagatesCancellation()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        var client = new UnconfiguredQueryExecutionClient();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.ExecuteAsync(CreatePlan(), source.Token));
    }

    [Fact]
    public void ExecutionPlan_RejectsUnsafeRequiredValues()
    {
        Assert.Throws<ArgumentException>(
            () => new SqlExecutionPlan(
                SqlDataSource.Dwh,
                " ",
                [],
                "customer_state = @region",
                30,
                null));
        Assert.Throws<ArgumentNullException>(
            () => new SqlExecutionPlan(
                SqlDataSource.Dwh,
                "SELECT 1",
                null!,
                "customer_state = @region",
                30,
                null));
        Assert.Throws<ArgumentException>(
            () => new SqlExecutionPlan(
                SqlDataSource.Dwh,
                "SELECT 1",
                [],
                " ",
                30,
                null));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SqlExecutionPlan(
                SqlDataSource.Dwh,
                "SELECT 1",
                [],
                "customer_state = @region",
                0,
                null));
    }

    [Fact]
    public void SensitiveModels_RedactToString()
    {
        var parameter = new SqlExecutionParameter(
            "@region",
            SqlExecutionParameterKind.Text,
            "secret-region",
            true);
        var plan = new SqlExecutionPlan(
            SqlDataSource.Dwh,
            "SELECT secret_column FROM secret_view",
            [parameter],
            "secret_scope_filter",
            30,
            null);

        Assert.DoesNotContain("secret", parameter.ToString());
        Assert.DoesNotContain("secret", plan.ToString());
        Assert.DoesNotContain("SELECT", plan.ToString());
    }

    private static SqlExecutionPlan CreatePlan() =>
        new(
            SqlDataSource.Dwh,
            "SELECT * FROM vw_sales WHERE customer_state = @region",
            [
                new SqlExecutionParameter(
                    "@region",
                    SqlExecutionParameterKind.Text,
                    "secret-region",
                    true)
            ],
            "customer_state = @region",
            30,
            null);
}
