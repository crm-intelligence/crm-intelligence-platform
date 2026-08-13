namespace CrmAnalytics.DataScopeProvisioner;

public readonly record struct ScopeKey(Guid TenantId, Guid UserId);

public sealed record ScopeState(
    long AssignmentCount,
    bool IsActive,
    bool AllowAllRegions,
    bool AllowAllStores,
    long RegionChildCount,
    long StoreChildCount)
{
    public bool IsValidUnrestricted =>
        AssignmentCount == 1
        && IsActive
        && AllowAllRegions
        && AllowAllStores
        && RegionChildCount == 0
        && StoreChildCount == 0;
}

public enum MutationDecision
{
    Insert,
    Update,
    NoOp
}

public sealed record MutationResult(MutationDecision Decision, ScopeState State);

public interface IDataScopeStore : IAsyncDisposable
{
    Task<ScopeState> ReadAsync(ScopeKey target, CancellationToken cancellationToken);

    Task<MutationResult> ApplyUnrestrictedAsync(
        ScopeKey target,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

public sealed class DataScopeProvisioner(IDataScopeStore store, TextWriter output)
{
    public async Task<int> RunAsync(
        ProvisionerOptions options,
        CancellationToken cancellationToken = default)
    {
        var maskedTarget = Masking.Mask(options.Target.UserId);
        var initial = await store.ReadAsync(options.Target, cancellationToken);
        output.WriteLine(
            $"Managed identity authentication succeeded; Database={options.SqlDatabase}; Target={maskedTarget}.");
        WriteState("Initial", initial);

        if (initial.IsValidUnrestricted)
        {
            output.WriteLine("Existing unrestricted scope is already valid; no write executed.");
            return 0;
        }

        if (options.DryRun)
        {
            output.WriteLine("DRY_RUN=true; target scope requires provisioning; no write executed.");
            return 0;
        }

        if (!options.ConfirmProductionWrite)
        {
            output.WriteLine("Production write refused because confirmation is false.");
            return 2;
        }

        var mutation = await store.ApplyUnrestrictedAsync(
            options.Target,
            DateTimeOffset.UtcNow,
            cancellationToken);
        output.WriteLine($"Transactional target-only mutation decision={mutation.Decision}.");

        var final = await store.ReadAsync(options.Target, cancellationToken);
        WriteState("Final", final);
        if (!final.IsValidUnrestricted)
        {
            output.WriteLine("Final target scope verification failed.");
            return 3;
        }

        output.WriteLine("Final target scope verification succeeded.");
        return 0;
    }

    private void WriteState(string label, ScopeState state) => output.WriteLine(
        $"{label}: AssignmentCount={state.AssignmentCount}; "
        + $"IsActive={state.IsActive}; "
        + $"AllowAllRegions={state.AllowAllRegions}; "
        + $"AllowAllStores={state.AllowAllStores}; "
        + $"RegionChildCount={state.RegionChildCount}; "
        + $"StoreChildCount={state.StoreChildCount}.");
}

public static class Masking
{
    public static string Mask(Guid value)
    {
        var text = value.ToString("D");
        return $"{text[..8]}-****-****-****-********{text[^4..]}";
    }
}
