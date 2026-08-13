using CrmAnalytics.DataScopeProvisioner;

namespace CrmAnalytics.DataScopeProvisioner.Tests;

public sealed class ProvisionerTests
{
    private static readonly Guid TenantId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserId =
        Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ClientId =
        Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public void InvalidTenantGuidIsRejected()
    {
        var values = ValidValues();
        values["TARGET_TENANT_ID"] = "not-a-guid";

        var parsed = Parse(values, out _, out var error);

        Assert.False(parsed);
        Assert.Contains("TARGET_TENANT_ID", error, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidUserGuidIsRejected()
    {
        var values = ValidValues();
        values["TARGET_USER_ID"] = "not-a-guid";

        var parsed = Parse(values, out _, out var error);

        Assert.False(parsed);
        Assert.Contains("TARGET_USER_ID", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionWriteWithoutConfirmationIsRejected()
    {
        var values = ValidValues();
        values["DRY_RUN"] = "false";
        values["CONFIRM_PRODUCTION_WRITE"] = "false";

        var parsed = Parse(values, out _, out var error);

        Assert.False(parsed);
        Assert.Contains("refused", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DryRunExecutesNoWrite()
    {
        var store = new FakeStore(MissingState());
        var result = await RunAsync(store, CreateOptions(dryRun: true, confirm: false));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(0, store.ApplyCount);
        Assert.Contains("no write executed", result.Log, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExistingValidScopeIsNoOp()
    {
        var store = new FakeStore(ValidState());
        var result = await RunAsync(store, CreateOptions(dryRun: false, confirm: true));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(0, store.ApplyCount);
    }

    [Fact]
    public async Task MissingScopeProducesInsertDecision()
    {
        var store = new FakeStore(MissingState(), MutationDecision.Insert);
        var result = await RunAsync(store, CreateOptions(dryRun: false, confirm: true));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(1, store.ApplyCount);
        Assert.Contains("decision=Insert", result.Log, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvalidScopeProducesUpdateDecision()
    {
        var invalid = ValidState() with { IsActive = false, RegionChildCount = 1 };
        var store = new FakeStore(invalid, MutationDecision.Update);
        var result = await RunAsync(store, CreateOptions(dryRun: false, confirm: true));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(1, store.ApplyCount);
        Assert.Contains("decision=Update", result.Log, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StoreCannotBeAskedToCreateAnotherUserKey()
    {
        var expected = new ScopeKey(TenantId, UserId);
        var store = new FakeStore(MissingState(), MutationDecision.Insert, expected);
        var options = CreateOptions(dryRun: false, confirm: true);

        var result = await RunAsync(store, options);

        Assert.Equal(0, result.ExitCode);
        Assert.All(store.ObservedKeys, key => Assert.Equal(expected, key));
    }

    [Fact]
    public async Task LogMaskingNeverShowsFullGuid()
    {
        var store = new FakeStore(ValidState());
        var result = await RunAsync(store, CreateOptions(dryRun: true, confirm: false));

        Assert.DoesNotContain(UserId.ToString("D"), result.Log, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("22222222-****", result.Log, StringComparison.Ordinal);
    }

    private static bool Parse(
        Dictionary<string, string> values,
        out ProvisionerOptions? options,
        out string error) => ProvisionerOptions.TryParse(
            [],
            name => values.GetValueOrDefault(name),
            out options,
            out error);

    private static Dictionary<string, string> ValidValues() => new()
    {
        ["SQL_SERVER"] = "example.database.windows.net",
        ["SQL_DATABASE"] = "Example",
        ["MANAGED_IDENTITY_CLIENT_ID"] = ClientId.ToString("D"),
        ["TARGET_TENANT_ID"] = TenantId.ToString("D"),
        ["TARGET_USER_ID"] = UserId.ToString("D"),
        ["ALLOW_ALL_REGIONS"] = "true",
        ["ALLOW_ALL_STORES"] = "true",
        ["DRY_RUN"] = "true",
        ["CONFIRM_PRODUCTION_WRITE"] = "false"
    };

    private static ProvisionerOptions CreateOptions(bool dryRun, bool confirm) => new(
        "example.database.windows.net",
        "Example",
        ClientId,
        new ScopeKey(TenantId, UserId),
        true,
        true,
        dryRun,
        confirm);

    private static ScopeState MissingState() => new(0, false, false, false, 0, 0);

    private static ScopeState ValidState() => new(1, true, true, true, 0, 0);

    private static async Task<(int ExitCode, string Log)> RunAsync(
        FakeStore store,
        ProvisionerOptions options)
    {
        using var writer = new StringWriter();
        var provisioner = new DataScopeProvisioner(store, writer);
        var exitCode = await provisioner.RunAsync(options);
        return (exitCode, writer.ToString());
    }

    private sealed class FakeStore : IDataScopeStore
    {
        private readonly ScopeState initial;
        private readonly MutationDecision decision;
        private readonly ScopeKey? expected;
        private bool applied;

        public FakeStore(
            ScopeState initial,
            MutationDecision decision = MutationDecision.NoOp,
            ScopeKey? expected = null)
        {
            this.initial = initial;
            this.decision = decision;
            this.expected = expected;
        }

        public int ApplyCount { get; private set; }

        public List<ScopeKey> ObservedKeys { get; } = [];

        public Task<ScopeState> ReadAsync(
            ScopeKey target,
            CancellationToken cancellationToken)
        {
            Observe(target);
            return Task.FromResult(applied ? ValidState() : initial);
        }

        public Task<MutationResult> ApplyUnrestrictedAsync(
            ScopeKey target,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            Observe(target);
            ApplyCount++;
            applied = true;
            return Task.FromResult(new MutationResult(decision, initial));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private void Observe(ScopeKey target)
        {
            ObservedKeys.Add(target);
            if (expected.HasValue && target != expected.Value)
            {
                throw new InvalidOperationException("Unexpected target key.");
            }
        }
    }
}
