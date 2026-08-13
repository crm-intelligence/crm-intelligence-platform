using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CrmAnalytics.Infrastructure.Identity;

internal static class UserDataAccessCollation
{
    public const string CaseInsensitive = "Latin1_General_100_CI_AS";
}

internal sealed class UserDataAccessAssignmentEntityConfiguration
    : IEntityTypeConfiguration<UserDataAccessAssignmentEntity>
{
    public void Configure(
        EntityTypeBuilder<UserDataAccessAssignmentEntity> builder)
    {
        builder.ToTable("UserDataAccessAssignments", "crm");
        builder.HasKey(value => new { value.TenantId, value.UserId });
        builder.Property(value => value.TenantId)
            .HasMaxLength(36).ValueGeneratedNever();
        builder.Property(value => value.UserId)
            .HasMaxLength(36).ValueGeneratedNever();
        builder.Property(value => value.CreatedAt)
            .HasColumnType("datetimeoffset(7)").IsRequired();
        builder.Property(value => value.UpdatedAt)
            .HasColumnType("datetimeoffset(7)").IsRequired();
        builder.Property(value => value.RowVersion)
            .IsRowVersion().IsRequired();
        builder.HasIndex(value => new { value.IsActive, value.TenantId });
        builder.HasIndex(value => value.UpdatedAt);
    }
}

internal sealed class UserDataAccessRegionEntityConfiguration
    : IEntityTypeConfiguration<UserDataAccessRegionEntity>
{
    public void Configure(
        EntityTypeBuilder<UserDataAccessRegionEntity> builder)
    {
        builder.ToTable("UserDataAccessRegions", "crm");
        builder.HasKey(value => new
        {
            value.TenantId,
            value.UserId,
            value.RegionCode
        });
        builder.Property(value => value.TenantId).HasMaxLength(36);
        builder.Property(value => value.UserId).HasMaxLength(36);
        builder.Property(value => value.RegionCode)
            .HasMaxLength(128)
            .UseCollation(UserDataAccessCollation.CaseInsensitive);
        builder.HasOne(value => value.Assignment)
            .WithMany(value => value.Regions)
            .HasForeignKey(value => new { value.TenantId, value.UserId })
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class UserDataAccessStoreEntityConfiguration
    : IEntityTypeConfiguration<UserDataAccessStoreEntity>
{
    public void Configure(
        EntityTypeBuilder<UserDataAccessStoreEntity> builder)
    {
        builder.ToTable("UserDataAccessStores", "crm");
        builder.HasKey(value => new
        {
            value.TenantId,
            value.UserId,
            value.StoreId
        });
        builder.Property(value => value.TenantId).HasMaxLength(36);
        builder.Property(value => value.UserId).HasMaxLength(36);
        builder.Property(value => value.StoreId)
            .HasMaxLength(128)
            .UseCollation(UserDataAccessCollation.CaseInsensitive);
        builder.HasOne(value => value.Assignment)
            .WithMany(value => value.Stores)
            .HasForeignKey(value => new { value.TenantId, value.UserId })
            .OnDelete(DeleteBehavior.Cascade);
    }
}
