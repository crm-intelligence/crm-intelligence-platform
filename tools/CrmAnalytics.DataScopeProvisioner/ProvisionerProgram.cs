namespace CrmAnalytics.DataScopeProvisioner;

public static class ProvisionerProgram
{
    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            if (!ProvisionerOptions.TryParse(
                    args,
                    Environment.GetEnvironmentVariable,
                    out var options,
                    out var error))
            {
                Console.Error.WriteLine(error);
                return 2;
            }

            await using var store = new SqlDataScopeStore(
                options!.SqlServer,
                options.SqlDatabase,
                options.ManagedIdentityClientId);
            var provisioner = new DataScopeProvisioner(store, Console.Out);
            return await provisioner.RunAsync(options);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"Provisioning failed: {exception.GetType().Name}.");
            return 1;
        }
    }
}
