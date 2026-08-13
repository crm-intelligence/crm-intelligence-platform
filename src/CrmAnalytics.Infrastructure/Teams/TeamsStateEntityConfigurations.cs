using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CrmAnalytics.Infrastructure.Teams;

internal sealed class TeamsNotificationTargetEntityConfiguration
    : IEntityTypeConfiguration<TeamsNotificationTargetEntity>
{
    public void Configure(EntityTypeBuilder<TeamsNotificationTargetEntity> b)
    {
        b.ToTable("TeamsNotificationTargets", "crm");
        b.HasKey(x => x.RequestId);
        b.Property(x => x.RequestId).HasMaxLength(32).IsRequired();
        b.Property(x => x.ConversationId).HasMaxLength(512).IsRequired();
        b.Property(x => x.CreatedAt).HasPrecision(7);
        b.Property(x => x.UpdatedAt).HasPrecision(7);
        b.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
    }
}

internal sealed class TeamsNotificationDeliveryEntityConfiguration
    : IEntityTypeConfiguration<TeamsNotificationDeliveryEntity>
{
    public void Configure(EntityTypeBuilder<TeamsNotificationDeliveryEntity> b)
    {
        b.ToTable("TeamsNotificationDeliveries", "crm");
        b.HasKey(x => x.DeliveryId);
        b.Property(x => x.DeliveryId).HasMaxLength(64).IsRequired();
        b.Property(x => x.RequestId).HasMaxLength(32).IsRequired();
        b.Property(x => x.NotificationStatus).HasMaxLength(64).IsRequired();
        b.Property(x => x.State).HasMaxLength(16).IsRequired();
        b.Property(x => x.LockOwner).HasMaxLength(256);
        b.Property(x => x.ReportUpdatedAt).HasPrecision(7);
        b.Property(x => x.LockedUntil).HasPrecision(7);
        b.Property(x => x.DeliveredAt).HasPrecision(7);
        b.Property(x => x.CreatedAt).HasPrecision(7);
        b.Property(x => x.UpdatedAt).HasPrecision(7);
        b.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
        b.HasIndex(x => new { x.RequestId, x.ReportUpdatedAt });
        b.HasIndex(x => new { x.State, x.LockedUntil });
    }
}

internal sealed class TeamsCardActionSubmissionEntityConfiguration
    : IEntityTypeConfiguration<TeamsCardActionSubmissionEntity>
{
    public void Configure(EntityTypeBuilder<TeamsCardActionSubmissionEntity> b)
    {
        b.ToTable("TeamsCardActionSubmissions", "crm");
        b.HasKey(x => x.ActionToken);
        b.Property(x => x.ActionToken).HasMaxLength(64).IsRequired();
        b.Property(x => x.RequestId).HasMaxLength(32).IsRequired();
        b.Property(x => x.ActionType).HasMaxLength(64).IsRequired();
        b.Property(x => x.State).HasMaxLength(16).IsRequired();
        b.Property(x => x.ResultRequestId).HasMaxLength(32);
        b.Property(x => x.LockOwner).HasMaxLength(256);
        b.Property(x => x.LockedUntil).HasPrecision(7);
        b.Property(x => x.CreatedAt).HasPrecision(7);
        b.Property(x => x.UpdatedAt).HasPrecision(7);
        b.Property(x => x.CompletedAt).HasPrecision(7);
        b.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
    }
}
