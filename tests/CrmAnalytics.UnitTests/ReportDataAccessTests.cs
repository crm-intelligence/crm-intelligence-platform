using CrmAnalytics.Application.Exceptions;
using CrmAnalytics.Application.Identity;
using CrmAnalytics.Infrastructure.Identity;
using Microsoft.Extensions.Options;

namespace CrmAnalytics.UnitTests;

public sealed class ReportDataAccessTests
{
    [Fact]
    public void Assignment_NormalizesAndDeduplicatesValues()
    {
        var assignment = new UserDataAccessAssignment(
            TestDataScopeFactory.TenantId.ToUpperInvariant(),
            TestDataScopeFactory.UserId.ToUpperInvariant(),
            false,
            false,
            [" Marmara ", "marmara", "Ege"],
            [" STORE-1 ", "store-1"]);

        Assert.Equal(TestDataScopeFactory.TenantId, assignment.TenantId);
        Assert.Equal(TestDataScopeFactory.UserId, assignment.UserId);
        Assert.Equal(["Marmara", "Ege"], assignment.AllowedRegions);
        Assert.Equal(["STORE-1"], assignment.AllowedStoreIds);
        Assert.DoesNotContain(
            TestDataScopeFactory.UserId,
            assignment.ToString(),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not-a-guid", "11111111-1111-4111-8111-111111111111")]
    [InlineData("22222222-2222-4222-8222-222222222222", "not-a-guid")]
    public void Assignment_RejectsInvalidIdentity(
        string tenantId,
        string userId)
    {
        Assert.Throws<ArgumentException>(() =>
            new UserDataAccessAssignment(
                tenantId,
                userId,
                true,
                false,
                [],
                []));
    }

    [Fact]
    public void Assignment_RejectsConflictsEmptyAndWildcard()
    {
        Assert.Throws<ArgumentException>(() => Assignment(
            allowAllRegions: true,
            regions: ["Marmara"]));
        Assert.Throws<ArgumentException>(() => Assignment(
            allowAllStores: true,
            stores: ["STORE-1"]));
        Assert.Throws<ArgumentException>(() => Assignment(
            regions: ["*"]));
        Assert.Throws<ArgumentException>(() => Assignment());
    }

    [Fact]
    public void OptionsValidator_AcceptsEmptyStore()
    {
        var result = new ReportDataAccessOptionsValidator().Validate(
            null,
            new ReportDataAccessOptions
            {
                Assignments = []
            });

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void OptionsValidator_RejectsDuplicateAndNullCollections()
    {
        var duplicate = ValidAssignmentOptions();
        var duplicateResult = new ReportDataAccessOptionsValidator()
            .Validate(
                null,
                new ReportDataAccessOptions
                {
                    Assignments =
                    [
                        duplicate,
                        ValidAssignmentOptions()
                    ]
                });
        var nullCollection = ValidAssignmentOptions();
        nullCollection.AllowedRegions = null;
        var nullResult = new ReportDataAccessOptionsValidator()
            .Validate(
                null,
                new ReportDataAccessOptions
                {
                    Assignments = [nullCollection]
                });

        Assert.True(duplicateResult.Failed);
        Assert.True(nullResult.Failed);
    }

    [Fact]
    public void Store_RequiresExactTenantAndUserAndSnapshotsConfiguration()
    {
        var configuredRegions = new[] { "Marmara" };
        var options = ValidAssignmentOptions();
        options.AllowAllRegions = false;
        options.AllowedRegions = configuredRegions;
        var store = Store(options);
        configuredRegions[0] = "Mutated";

        var found = store.Find(
            TestDataScopeFactory.TenantId,
            TestDataScopeFactory.UserId);

        Assert.NotNull(found);
        Assert.Equal(["Marmara"], found.AllowedRegions);
        Assert.Null(store.Find(
            "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
            TestDataScopeFactory.UserId));
        Assert.Null(store.Find(
            TestDataScopeFactory.TenantId,
            "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"));
    }

    [Fact]
    public void Resolver_UsesAssignmentAndVerifiedRoles()
    {
        var options = ValidAssignmentOptions();
        options.AllowAllRegions = false;
        options.AllowAllStores = false;
        options.AllowedRegions = ["Marmara", "Ege"];
        options.AllowedStoreIds = ["STORE-1"];
        var resolver = new CurrentUserDataScopeResolver(Store(options));
        var user = new AuthenticatedUserContext(
            TestDataScopeFactory.UserId,
            TestDataScopeFactory.TenantId,
            ["Report.Viewer"]);

        var scope = resolver.ResolveRequired(user);

        Assert.Equal(user.UserId, scope.UserId);
        Assert.Equal(user.TenantId, scope.TenantId);
        Assert.Equal(["Report.Viewer"], scope.Roles);
        Assert.Equal(["Marmara", "Ege"], scope.AllowedRegions);
        Assert.Equal(["STORE-1"], scope.AllowedStoreIds);
        Assert.False(scope.AllowAllRegions);
        Assert.False(scope.AllowAllStores);
    }

    [Fact]
    public void Resolver_MissingAssignmentIsSafeForbidden()
    {
        var resolver = new CurrentUserDataScopeResolver(
            Store());
        var user = new AuthenticatedUserContext(
            TestDataScopeFactory.UserId,
            TestDataScopeFactory.TenantId,
            ["Report.User"]);

        var exception = Assert.Throws<ForbiddenAccessException>(
            () => resolver.ResolveRequired(user));

        Assert.Equal(ForbiddenAccessException.SafeMessage, exception.Message);
    }

    private static UserDataAccessAssignment Assignment(
        bool allowAllRegions = false,
        bool allowAllStores = false,
        IReadOnlyCollection<string>? regions = null,
        IReadOnlyCollection<string>? stores = null) =>
        new(
            TestDataScopeFactory.TenantId,
            TestDataScopeFactory.UserId,
            allowAllRegions,
            allowAllStores,
            regions ?? [],
            stores ?? []);

    private static ConfigurationUserDataAccessAssignmentStore Store(
        params ReportDataAccessAssignmentOptions[] assignments) =>
        new(Options.Create(new ReportDataAccessOptions
        {
            Assignments = assignments
        }));

    private static ReportDataAccessAssignmentOptions
        ValidAssignmentOptions() =>
        new()
        {
            TenantId = TestDataScopeFactory.TenantId,
            UserId = TestDataScopeFactory.UserId,
            AllowAllRegions = true,
            AllowAllStores = true,
            AllowedRegions = [],
            AllowedStoreIds = []
        };
}
