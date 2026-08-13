using CrmAnalytics.Application.Authorization;
using CrmAnalytics.Application.Exceptions;
using CrmAnalytics.Application.Identity;
using CrmAnalytics.Infrastructure.Authorization;
using Microsoft.Extensions.Options;

namespace CrmAnalytics.UnitTests;

public sealed class ReportAuthorizationTests
{
    [Fact]
    public void OptionsValidator_AcceptsDefaultMappings()
    {
        var result = Validator().Validate(null, ValidOptions());

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void OptionsValidator_RejectsEmptyMappings()
    {
        var result = Validator().Validate(
            null,
            new ReportAuthorizationOptions
            {
                RolePermissions =
                    new Dictionary<string, string[]?>()
            });

        Assert.True(result.Failed);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" Report.User")]
    [InlineData("Report.User ")]
    public void OptionsValidator_RejectsInvalidRoleName(string role)
    {
        var options = ValidOptions();
        options.RolePermissions!.Add(role, ["Create"]);

        Assert.True(Validator().Validate(null, options).Failed);
    }

    [Theory]
    [InlineData("Unknown")]
    [InlineData("*")]
    [InlineData("All")]
    [InlineData("create")]
    [InlineData("0")]
    public void OptionsValidator_RejectsUnknownPermission(
        string permission)
    {
        var options = ValidOptions();
        options.RolePermissions![ReportAppRoles.User] = [permission];

        Assert.True(Validator().Validate(null, options).Failed);
    }

    [Fact]
    public void OptionsValidator_RejectsDuplicatePermission()
    {
        var options = ValidOptions();
        options.RolePermissions![ReportAppRoles.User] =
            ["Create", "Create", "ReadOwn"];

        Assert.True(Validator().Validate(null, options).Failed);
    }

    [Fact]
    public void OptionsValidator_RejectsCaseInsensitiveDuplicateRole()
    {
        var options = ValidOptions();
        options.RolePermissions!.Add(
            "report.user",
            ["Create", "ReadOwn"]);

        Assert.True(Validator().Validate(null, options).Failed);
    }

    [Theory]
    [InlineData("Create")]
    [InlineData("ReadOwn")]
    public void OptionsValidator_RequiresCorePermission(
        string permissionToRemove)
    {
        var options = ValidOptions();
        foreach (var role in options.RolePermissions!.Keys.ToArray())
        {
            options.RolePermissions[role] = options
                .RolePermissions[role]!
                .Where(value => value != permissionToRemove)
                .ToArray();
        }

        Assert.True(Validator().Validate(null, options).Failed);
    }

    [Fact]
    public void Evaluator_CombinesRolesAndMatchesCaseSensitively()
    {
        var evaluator = Evaluator();
        var combined = User(
            ReportAppRoles.Viewer,
            ReportAppRoles.User);

        Assert.True(evaluator.HasPermission(
            combined,
            ReportPermission.Create));
        Assert.True(evaluator.HasPermission(
            combined,
            ReportPermission.ReadOwn));
        Assert.False(evaluator.HasPermission(
            User("report.user"),
            ReportPermission.Create));
        Assert.False(evaluator.HasPermission(
            User("Unknown", "wids", "groups"),
            ReportPermission.ReadOwn));
    }

    [Theory]
    [InlineData("Report.User", ReportPermission.Create, true)]
    [InlineData("Report.Viewer", ReportPermission.Create, false)]
    [InlineData("Report.Viewer", ReportPermission.ReadOwn, true)]
    [InlineData("Report.Admin", ReportPermission.ClarifyOwn, true)]
    public void Evaluator_UsesConfiguredPermissions(
        string role,
        ReportPermission permission,
        bool expected)
    {
        Assert.Equal(
            expected,
            Evaluator().HasPermission(User(role), permission));
    }

    [Fact]
    public void Evaluator_DenialUsesSafeException()
    {
        var exception = Assert.Throws<ForbiddenAccessException>(
            () => Evaluator().EnsureHasPermission(
                User("Secret.Role"),
                ReportPermission.Create));

        Assert.Equal(ForbiddenAccessException.SafeMessage, exception.Message);
        Assert.DoesNotContain(
            TestDataScopeFactory.UserId,
            exception.ToString(),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            TestDataScopeFactory.TenantId,
            exception.ToString(),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Secret.Role",
            exception.ToString(),
            StringComparison.Ordinal);
    }

    private static ReportAuthorizationOptionsValidator Validator() =>
        new();

    private static ReportPermissionEvaluator Evaluator() =>
        new(new ConfigurationReportRolePermissionPolicy(
            Options.Create(ValidOptions())));

    private static AuthenticatedUserContext User(params string[] roles) =>
        new(
            TestDataScopeFactory.UserId,
            TestDataScopeFactory.TenantId,
            roles);

    private static ReportAuthorizationOptions ValidOptions() =>
        new()
        {
            RolePermissions =
                new Dictionary<string, string[]?>(
                    StringComparer.Ordinal)
                {
                    [ReportAppRoles.User] =
                    [
                        "Create",
                        "ReadOwn",
                        "ReviseOwn",
                        "ClarifyOwn",
                        "ViewOwnHistory"
                    ],
                    [ReportAppRoles.Viewer] =
                    [
                        "ReadOwn",
                        "ViewOwnHistory"
                    ],
                    [ReportAppRoles.Admin] =
                    [
                        "Create",
                        "ReadOwn",
                        "ReviseOwn",
                        "ClarifyOwn",
                        "ViewOwnHistory"
                    ]
                }
        };
}
