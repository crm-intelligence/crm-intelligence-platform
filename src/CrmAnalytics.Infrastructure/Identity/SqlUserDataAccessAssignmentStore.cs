using CrmAnalytics.Application.Exceptions;
using CrmAnalytics.Application.Identity;
using CrmAnalytics.Infrastructure.Persistence.SqlServer;
using Microsoft.EntityFrameworkCore;
using System.Data.Common;

namespace CrmAnalytics.Infrastructure.Identity;

public sealed class SqlUserDataAccessAssignmentStore
    : IUserDataAccessAssignmentStore
{
    private readonly CrmAnalyticsDbContext _dbContext;

    public SqlUserDataAccessAssignmentStore(CrmAnalyticsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public UserDataAccessAssignment? Find(string tenantId, string userId)
    {
        var (tenant, user) = NormalizeIdentity(tenantId, userId);
        var entity = Query(tenant, user).SingleOrDefault();
        return entity is null ? null : MapRequired(entity);
    }

    public async Task<UserDataAccessAssignment?> FindAsync(
        string tenantId,
        string userId,
        CancellationToken cancellationToken)
    {
        var (tenant, user) = NormalizeIdentity(tenantId, userId);
        UserDataAccessAssignmentEntity? entity;
        try
        {
            entity = await Query(tenant, user).SingleOrDefaultAsync(
                cancellationToken);
        }
        catch (DbException)
        {
            throw new DataAccessPolicyIntegrityException();
        }
        return entity is null ? null : MapRequired(entity);
    }

    public async Task<UserDataAccessAssignmentInspection> InspectAsync(
        string tenantId,
        string userId,
        CancellationToken cancellationToken)
    {
        var (tenant, user) = NormalizeIdentity(tenantId, userId);
        UserDataAccessAssignmentEntity? entity;
        try
        {
            entity = await QueryIncludingInactive(tenant, user)
                .SingleOrDefaultAsync(cancellationToken);
        }
        catch (DbException)
        {
            return new UserDataAccessAssignmentInspection(
                false,
                false,
                false,
                false,
                false);
        }

        if (entity is null)
        {
            return UserDataAccessAssignmentInspection.NotFound;
        }

        try
        {
            _ = MapRequired(entity);
        }
        catch (DataAccessPolicyIntegrityException)
        {
            return new UserDataAccessAssignmentInspection(
                true,
                entity.IsActive,
                entity.AllowAllRegions,
                entity.AllowAllStores,
                false);
        }

        return new UserDataAccessAssignmentInspection(
            true,
            entity.IsActive,
            entity.AllowAllRegions,
            entity.AllowAllStores,
            true);
    }

    private IQueryable<UserDataAccessAssignmentEntity> Query(
        string tenantId,
        string userId) =>
        QueryIncludingInactive(tenantId, userId)
            .Where(value => value.IsActive);

    private IQueryable<UserDataAccessAssignmentEntity> QueryIncludingInactive(
        string tenantId,
        string userId) =>
        _dbContext.Set<UserDataAccessAssignmentEntity>()
            .AsNoTracking()
            .Include(value => value.Regions)
            .Include(value => value.Stores)
            .Where(value => value.TenantId == tenantId
                && value.UserId == userId);

    private static UserDataAccessAssignment MapRequired(
        UserDataAccessAssignmentEntity entity)
    {
        try
        {
            if (!Guid.TryParse(entity.TenantId, out var tenantId)
                || !Guid.TryParse(entity.UserId, out var userId)
                || entity.TenantId != tenantId.ToString("D")
                || entity.UserId != userId.ToString("D"))
            {
                throw new DataAccessPolicyIntegrityException();
            }

            var regions = ValidateValues(
                entity.Regions.Select(value => value.RegionCode));
            var stores = ValidateValues(
                entity.Stores.Select(value => value.StoreId));

            if ((entity.AllowAllRegions && regions.Count != 0)
                || (entity.AllowAllStores && stores.Count != 0))
            {
                throw new DataAccessPolicyIntegrityException();
            }

            return new UserDataAccessAssignment(
                entity.TenantId,
                entity.UserId,
                entity.AllowAllRegions,
                entity.AllowAllStores,
                regions,
                stores);
        }
        catch (DataAccessPolicyIntegrityException)
        {
            throw;
        }
        catch (ArgumentException)
        {
            throw new DataAccessPolicyIntegrityException();
        }
    }

    private static IReadOnlyCollection<string> ValidateValues(
        IEnumerable<string> values)
    {
        var result = values.Select(value => value?.Trim()).ToArray();
        if (result.Any(value => string.IsNullOrWhiteSpace(value)
            || string.Equals(value, "*", StringComparison.Ordinal))
            || result.Distinct(StringComparer.OrdinalIgnoreCase).Count()
                != result.Length)
        {
            throw new DataAccessPolicyIntegrityException();
        }

        return Array.AsReadOnly(result.Select(value => value!).ToArray());
    }

    private static (string TenantId, string UserId) NormalizeIdentity(
        string tenantId,
        string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        if (!Guid.TryParse(tenantId.Trim(), out var tenant)
            || !Guid.TryParse(userId.Trim(), out var user))
        {
            throw new ArgumentException(
                "Tenant and user identities must be valid GUID values.");
        }

        return (tenant.ToString("D"), user.ToString("D"));
    }
}
