using CrmAnalytics.Domain.Conversations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CrmAnalytics.Infrastructure.Persistence.SqlServer.Configurations;

public sealed class ConversationConfiguration
    : IEntityTypeConfiguration<Conversation>
{
    private const string CaseInsensitiveCollation =
        "Latin1_General_100_CI_AS";

    public void Configure(EntityTypeBuilder<Conversation> builder)
    {
        builder.ToTable("Conversations", "crm");

        builder.HasKey(conversation => conversation.Id);

        builder.Property(conversation => conversation.Id)
            .HasMaxLength(32)
            .ValueGeneratedNever();
        builder.Property(conversation => conversation.TeamsConversationId)
            .HasMaxLength(512)
            .UseCollation(CaseInsensitiveCollation)
            .IsRequired();
        builder.Property(conversation => conversation.UserId)
            .HasMaxLength(36)
            .IsRequired();
        builder.Property(conversation => conversation.TenantId)
            .HasMaxLength(36)
            .IsRequired();
        builder.Property(conversation => conversation.LastRequestId)
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(conversation => conversation.CreatedAt)
            .HasColumnType("datetimeoffset(7)")
            .IsRequired();
        builder.Property(conversation => conversation.UpdatedAt)
            .HasColumnType("datetimeoffset(7)")
            .IsRequired();

        builder.Property<byte[]>("RowVersion")
            .IsRowVersion()
            .IsRequired();

        builder.HasIndex(conversation =>
                conversation.TeamsConversationId)
            .IsUnique();
        builder.HasIndex(conversation => new
        {
            conversation.TenantId,
            conversation.UserId,
            conversation.TeamsConversationId
        });
        builder.HasIndex(conversation => conversation.LastRequestId);
        builder.HasIndex(conversation => new
        {
            conversation.TenantId,
            conversation.UserId,
            conversation.UpdatedAt
        });
    }
}
