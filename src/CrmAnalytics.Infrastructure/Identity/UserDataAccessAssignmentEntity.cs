namespace CrmAnalytics.Infrastructure.Identity;

internal sealed class UserDataAccessAssignmentEntity
{
    public string TenantId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public bool AllowAllRegions { get; set; }
    public bool AllowAllStores { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
    public ICollection<UserDataAccessRegionEntity> Regions { get; set; } =
        new List<UserDataAccessRegionEntity>();
    public ICollection<UserDataAccessStoreEntity> Stores { get; set; } =
        new List<UserDataAccessStoreEntity>();
}

internal sealed class UserDataAccessRegionEntity
{
    public string TenantId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string RegionCode { get; set; } = string.Empty;
    public UserDataAccessAssignmentEntity Assignment { get; set; } = null!;
}

internal sealed class UserDataAccessStoreEntity
{
    public string TenantId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string StoreId { get; set; } = string.Empty;
    public UserDataAccessAssignmentEntity Assignment { get; set; } = null!;
}
