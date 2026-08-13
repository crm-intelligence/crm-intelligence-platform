using CrmAnalytics.Domain.ReportRequests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CrmAnalytics.Infrastructure.Persistence.SqlServer.Configurations;

public sealed class ReportRequestConfiguration
    : IEntityTypeConfiguration<ReportRequest>
{
    public void Configure(EntityTypeBuilder<ReportRequest> builder)
    {
        builder.ToTable("ReportRequests", "crm");

        builder.HasKey(reportRequest => reportRequest.RequestId);

        builder.Property(reportRequest => reportRequest.RequestId)
            .HasMaxLength(32)
            .ValueGeneratedNever();
        builder.Property(reportRequest => reportRequest.ConversationId)
            .HasMaxLength(512)
            .IsRequired();
        builder.Property(reportRequest => reportRequest.PreviousRequestId)
            .HasMaxLength(32);
        builder.Property(reportRequest => reportRequest.Prompt)
            .HasMaxLength(2000)
            .IsRequired();
        builder.Property(reportRequest => reportRequest.CorrelationId)
            .HasMaxLength(128)
            .IsRequired();
        builder.Property(reportRequest => reportRequest.UserId)
            .HasMaxLength(36)
            .IsRequired();
        builder.Property(reportRequest => reportRequest.TenantId)
            .HasMaxLength(36)
            .IsRequired();
        builder.Property(reportRequest => reportRequest.Status)
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired();
        builder.Property(reportRequest => reportRequest.ReportId)
            .HasMaxLength(256);
        builder.Property(reportRequest => reportRequest.Summary)
            .HasMaxLength(4000);
        builder.Property(reportRequest => reportRequest.PowerBiUrl)
            .HasMaxLength(2048);
        builder.Property(reportRequest => reportRequest.ClarificationQuestion)
            .HasMaxLength(1000);
        builder.Property(reportRequest => reportRequest.ClarificationResponse)
            .HasMaxLength(2000);
        builder.Property(reportRequest => reportRequest.ErrorCode)
            .HasMaxLength(128);
        builder.Property(reportRequest => reportRequest.ErrorMessage)
            .HasMaxLength(1000);
        builder.Property(reportRequest => reportRequest.RejectionCode)
            .HasMaxLength(128);
        builder.Property(reportRequest => reportRequest.RejectionMessage)
            .HasMaxLength(1000);
        builder.Property(reportRequest => reportRequest.CanonicalRequestJson)
            .HasColumnType("nvarchar(max)");
        builder.Property(reportRequest => reportRequest.SemanticPlanJson)
            .HasColumnType("nvarchar(max)");
        builder.Property(reportRequest => reportRequest.ReferenceDate)
            .HasColumnType("date")
            .IsRequired();
        builder.Property(reportRequest => reportRequest.CreatedAt)
            .HasColumnType("datetimeoffset(7)")
            .IsRequired();
        builder.Property(reportRequest => reportRequest.UpdatedAt)
            .HasColumnType("datetimeoffset(7)")
            .IsRequired();

        builder.Property<byte[]>("RowVersion")
            .IsRowVersion()
            .IsRequired();

        builder.HasIndex(reportRequest => new
        {
            reportRequest.TenantId,
            reportRequest.UserId,
            reportRequest.RequestId
        });
        builder.HasIndex(reportRequest => new
        {
            reportRequest.TenantId,
            reportRequest.UserId,
            reportRequest.ConversationId,
            reportRequest.CreatedAt
        });
        builder.HasIndex(reportRequest => new
        {
            reportRequest.ConversationId,
            reportRequest.CreatedAt
        });
        builder.HasIndex(reportRequest => reportRequest.PreviousRequestId);
        builder.HasIndex(reportRequest => new
        {
            reportRequest.Status,
            reportRequest.UpdatedAt
        });
    }
}
