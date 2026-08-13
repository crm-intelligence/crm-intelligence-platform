using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CrmAnalytics.Infrastructure.Auditing;

internal sealed class ApplicationAuditEventEntityConfiguration
    : IEntityTypeConfiguration<ApplicationAuditEventEntity>
{
    public void Configure(
        EntityTypeBuilder<ApplicationAuditEventEntity> builder)
    {
        builder.ToTable("ApplicationAuditEvents", "crm");
        builder.HasKey(value => value.EventId);
        builder.Property(value => value.EventId)
            .HasMaxLength(64).ValueGeneratedNever();
        builder.Property(value => value.EventType)
            .HasMaxLength(128).IsRequired();
        builder.Property(value => value.Outcome)
            .HasMaxLength(32).IsRequired();
        builder.Property(value => value.OccurredAt)
            .HasColumnType("datetimeoffset(7)").IsRequired();
        builder.Property(value => value.RequestId).HasMaxLength(32);
        builder.Property(value => value.PreviousRequestId).HasMaxLength(32);
        builder.Property(value => value.CorrelationId).HasMaxLength(128);
        builder.Property(value => value.ActorUserId).HasMaxLength(36);
        builder.Property(value => value.TenantId).HasMaxLength(36);
        builder.Property(value => value.ReportStatus).HasMaxLength(64);
        builder.Property(value => value.ReasonCode).HasMaxLength(128);
        builder.Property(value => value.DataSource).HasMaxLength(32);
        builder.Property(value => value.AuditMetadataJson)
            .HasColumnType("nvarchar(max)");

        builder.HasIndex(value => value.OccurredAt);
        builder.HasIndex(value => new { value.RequestId, value.OccurredAt });
        builder.HasIndex(value => new
        {
            value.TenantId,
            value.ActorUserId,
            value.OccurredAt
        });
        builder.HasIndex(value => new { value.EventType, value.OccurredAt });
        builder.HasIndex(value => value.CorrelationId);
    }
}
