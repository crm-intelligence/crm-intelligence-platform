namespace CrmAnalytics.DataScopeProvisioner;

public sealed record ProvisionerOptions(
    string SqlServer,
    string SqlDatabase,
    Guid ManagedIdentityClientId,
    ScopeKey Target,
    bool AllowAllRegions,
    bool AllowAllStores,
    bool DryRun,
    bool ConfirmProductionWrite)
{
    private static readonly HashSet<string> AllowedNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "SQL_SERVER",
            "SQL_DATABASE",
            "MANAGED_IDENTITY_CLIENT_ID",
            "TARGET_TENANT_ID",
            "TARGET_USER_ID",
            "ALLOW_ALL_REGIONS",
            "ALLOW_ALL_STORES",
            "DRY_RUN",
            "CONFIRM_PRODUCTION_WRITE"
        };

    public static bool TryParse(
        IReadOnlyList<string> args,
        Func<string, string?> environment,
        out ProvisionerOptions? options,
        out string error)
    {
        options = null;
        error = string.Empty;
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in AllowedNames)
        {
            var value = environment(name);
            if (!string.IsNullOrWhiteSpace(value))
            {
                values[name] = value;
            }
        }

        for (var index = 0; index < args.Count; index++)
        {
            var argument = args[index];
            if (argument.StartsWith("--", StringComparison.Ordinal))
            {
                argument = argument[2..];
            }

            var separator = argument.IndexOf('=');
            string name;
            string value;
            if (separator >= 0)
            {
                name = argument[..separator];
                value = argument[(separator + 1)..];
            }
            else
            {
                name = argument;
                if (index + 1 >= args.Count)
                {
                    error = $"Missing value for {name}.";
                    return false;
                }

                value = args[++index];
            }

            if (!AllowedNames.Contains(name))
            {
                error = $"Unsupported option {name}.";
                return false;
            }

            values[name] = value;
        }

        if (!TryRequired(values, "SQL_SERVER", out var sqlServer, out error)
            || !TryRequired(values, "SQL_DATABASE", out var sqlDatabase, out error)
            || !TryGuid(values, "MANAGED_IDENTITY_CLIENT_ID", out var clientId, out error)
            || !TryGuid(values, "TARGET_TENANT_ID", out var tenantId, out error)
            || !TryGuid(values, "TARGET_USER_ID", out var userId, out error)
            || !TryBoolean(values, "ALLOW_ALL_REGIONS", out var allowAllRegions, out error)
            || !TryBoolean(values, "ALLOW_ALL_STORES", out var allowAllStores, out error)
            || !TryBoolean(values, "DRY_RUN", out var dryRun, out error)
            || !TryBoolean(values, "CONFIRM_PRODUCTION_WRITE", out var confirm, out error))
        {
            return false;
        }

        if (!allowAllRegions || !allowAllStores)
        {
            error = "This tool provisions unrestricted scope only; both allow-all flags must be true.";
            return false;
        }

        if (!dryRun && !confirm)
        {
            error = "Production write refused: CONFIRM_PRODUCTION_WRITE must be true.";
            return false;
        }

        options = new ProvisionerOptions(
            sqlServer,
            sqlDatabase,
            clientId,
            new ScopeKey(tenantId, userId),
            allowAllRegions,
            allowAllStores,
            dryRun,
            confirm);
        return true;
    }

    private static bool TryRequired(
        IReadOnlyDictionary<string, string> values,
        string name,
        out string value,
        out string error)
    {
        if (values.TryGetValue(name, out value!) && !string.IsNullOrWhiteSpace(value))
        {
            value = value.Trim();
            error = string.Empty;
            return true;
        }

        value = string.Empty;
        error = $"Missing required option {name}.";
        return false;
    }

    private static bool TryGuid(
        IReadOnlyDictionary<string, string> values,
        string name,
        out Guid value,
        out string error)
    {
        value = default;
        if (!TryRequired(values, name, out var raw, out error))
        {
            return false;
        }

        if (Guid.TryParseExact(raw, "D", out value) && value != Guid.Empty)
        {
            return true;
        }

        error = $"Option {name} must be a non-empty GUID in D format.";
        return false;
    }

    private static bool TryBoolean(
        IReadOnlyDictionary<string, string> values,
        string name,
        out bool value,
        out string error)
    {
        value = default;
        if (!TryRequired(values, name, out var raw, out error))
        {
            return false;
        }

        if (bool.TryParse(raw, out value))
        {
            return true;
        }

        error = $"Option {name} must be true or false.";
        return false;
    }
}
