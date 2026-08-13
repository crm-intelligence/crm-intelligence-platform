using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CrmAnalytics.Infrastructure.Outbox;

internal sealed class OutboxMessageEntityConfiguration
    : IEntityTypeConfiguration<OutboxMessageEntity>
{
    public void Configure(EntityTypeBuilder<OutboxMessageEntity> builder)
    {
        builder.ToTable("OutboxMessages", "crm");
        builder.HasKey(item => item.MessageId);
        builder.Property(item => item.MessageId).HasMaxLength(64);
        builder.Property(item => item.MessageType).HasMaxLength(128).IsRequired();
        builder.Property(item => item.AggregateId).HasMaxLength(64).IsRequired();
        builder.Property(item => item.OccurredAt).HasColumnType("datetimeoffset(7)");
        builder.Property(item => item.PayloadJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(item => item.Status).HasMaxLength(32).IsRequired();
        builder.Property(item => item.NextAttemptAt).HasColumnType("datetimeoffset(7)");
        builder.Property(item => item.PublishedAt).HasColumnType("datetimeoffset(7)");
        builder.Property(item => item.DeadLetteredAt).HasColumnType("datetimeoffset(7)");
        builder.Property(item => item.LockOwner).HasMaxLength(128);
        builder.Property(item => item.LockedUntil).HasColumnType("datetimeoffset(7)");
        builder.Property(item => item.LastFailureCode).HasMaxLength(128);
        builder.Property(item => item.CreatedAt).HasColumnType("datetimeoffset(7)");
        builder.Property(item => item.UpdatedAt).HasColumnType("datetimeoffset(7)");
        builder.Property(item => item.RowVersion).IsRowVersion().IsConcurrencyToken();

        builder.HasIndex(item => new { item.Status, item.NextAttemptAt, item.OccurredAt });
        builder.HasIndex(item => item.LockedUntil);
        builder.HasIndex(item => new { item.AggregateId, item.OccurredAt });
        builder.HasIndex(item => item.PublishedAt);
        builder.HasIndex(item => item.DeadLetteredAt);
    }
}
