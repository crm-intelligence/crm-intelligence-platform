using CrmAnalytics.Application.Identity;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using CrmAnalytics.Infrastructure.Persistence;

namespace CrmAnalytics.Infrastructure.Identity;

public sealed class ReportDataAccessOptionsValidator
    : IValidateOptions<ReportDataAccessOptions>
{
    public const int MaximumAssignments = 10_000;
    private readonly IConfiguration? _configuration;
    private readonly IHostEnvironment? _environment;

    public ReportDataAccessOptionsValidator(
        IConfiguration? configuration = null,
        IHostEnvironment? environment = null)
    {
        _configuration = configuration;
        _environment = environment;
    }

    public ValidateOptionsResult Validate(
        string? name,
        ReportDataAccessOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();

        var isConfiguration = string.Equals(
            options.Provider?.Trim(),
            ReportDataAccessOptions.ConfigurationProvider,
            StringComparison.OrdinalIgnoreCase);
        var isSqlServer = string.Equals(
            options.Provider?.Trim(),
            ReportDataAccessOptions.SqlServerProvider,
            StringComparison.OrdinalIgnoreCase);

        if (!isConfiguration && !isSqlServer)
        {
            failures.Add(
                "ReportDataAccess:Provider must be Configuration or SqlServer.");
        }

        if (isConfiguration && _environment is not null
            && !_environment.IsDevelopment()
            && !_environment.IsEnvironment("Test")
            && !_environment.IsEnvironment("Testing"))
        {
            failures.Add(
                "The Configuration data-access provider is allowed only "
                + "in Development or test environments.");
        }

        if (isSqlServer && _configuration is not null
            && !PersistenceOptionsValidator.IsSqlServer(
                _configuration[$"{PersistenceOptions.SectionName}:Provider"]))
        {
            failures.Add(
                "The SqlServer data-access provider requires SqlServer persistence.");
        }

        if (options.Assignments is null)
        {
            return ValidateOptionsResult.Fail(
                "ReportDataAccess:Assignments must not be null.");
        }

        if (options.Assignments.Length > MaximumAssignments)
        {
            failures.Add(
                $"ReportDataAccess supports at most {MaximumAssignments} "
                + "configuration assignments.");
        }

        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var assignment in options.Assignments)
        {
            if (assignment is null)
            {
                failures.Add("Assignments cannot contain null entries.");
                continue;
            }

            if (assignment.AllowedRegions is null
                || assignment.AllowedStoreIds is null)
            {
                failures.Add(
                    "Assignment region and store collections must not "
                    + "be null.");
                continue;
            }

            try
            {
                var normalized = new UserDataAccessAssignment(
                    assignment.TenantId ?? string.Empty,
                    assignment.UserId ?? string.Empty,
                    assignment.AllowAllRegions,
                    assignment.AllowAllStores,
                    assignment.AllowedRegions,
                    assignment.AllowedStoreIds);
                var key = $"{normalized.TenantId}|{normalized.UserId}";

                if (!identities.Add(key))
                {
                    failures.Add(
                        "Duplicate tenant and user assignments are not "
                        + "allowed.");
                }
            }
            catch (ArgumentException)
            {
                failures.Add(
                    "A ReportDataAccess assignment is invalid.");
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
