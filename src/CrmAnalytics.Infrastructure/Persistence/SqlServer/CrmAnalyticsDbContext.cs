using CrmAnalytics.Domain.Conversations;
using CrmAnalytics.Domain.ReportRequests;
using CrmAnalytics.Infrastructure.Auditing;
using CrmAnalytics.Infrastructure.Identity;
using CrmAnalytics.Infrastructure.Outbox;
using CrmAnalytics.Infrastructure.Teams;
using Microsoft.EntityFrameworkCore;

namespace CrmAnalytics.Infrastructure.Persistence.SqlServer;

public sealed class CrmAnalyticsDbContext : DbContext
{
    public CrmAnalyticsDbContext(
        DbContextOptions<CrmAnalyticsDbContext> options)
        : base(options)
    {
    }

    public DbSet<ReportRequest> ReportRequests => Set<ReportRequest>();

    public DbSet<Conversation> Conversations => Set<Conversation>();

    internal DbSet<OutboxMessageEntity> OutboxMessages =>
        Set<OutboxMessageEntity>();

    internal DbSet<TeamsNotificationTargetEntity> TeamsNotificationTargets =>
        Set<TeamsNotificationTargetEntity>();

    internal DbSet<TeamsNotificationDeliveryEntity> TeamsNotificationDeliveries =>
        Set<TeamsNotificationDeliveryEntity>();

    internal DbSet<TeamsCardActionSubmissionEntity> TeamsCardActionSubmissions =>
        Set<TeamsCardActionSubmissionEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(CrmAnalyticsDbContext).Assembly);

        if (string.Equals(
            Database.ProviderName,
            "Microsoft.EntityFrameworkCore.SqlServer",
            StringComparison.Ordinal))
        {
            modelBuilder.Entity<ApplicationAuditEventEntity>().ToTable(
                "ApplicationAuditEvents",
                "crm",
                table => table.HasCheckConstraint(
                    "CK_ApplicationAuditEvents_AuditMetadataJson_IsJson",
                    "[AuditMetadataJson] IS NULL OR ISJSON([AuditMetadataJson]) = 1"));
        }

        if (string.Equals(
            Database.ProviderName,
            "Microsoft.EntityFrameworkCore.Sqlite",
            StringComparison.Ordinal))
        {
            var reportRequest = modelBuilder.Entity<ReportRequest>();
            reportRequest
                .Property<byte[]>("RowVersion")
                .HasColumnType("BLOB")
                .HasDefaultValueSql("randomblob(8)");
            reportRequest
                .Property(item => item.CanonicalRequestJson)
                .HasColumnType("TEXT");
            reportRequest
                .Property(item => item.SemanticPlanJson)
                .HasColumnType("TEXT");
            reportRequest
                .Property(item => item.ReferenceDate)
                .HasColumnType("TEXT");
            reportRequest
                .Property(item => item.CreatedAt)
                .HasConversion<long>()
                .HasColumnType("INTEGER");
            reportRequest
                .Property(item => item.UpdatedAt)
                .HasConversion<long>()
                .HasColumnType("INTEGER");

            var conversation = modelBuilder.Entity<Conversation>();
            conversation
                .Property<byte[]>("RowVersion")
                .HasColumnType("BLOB")
                .HasDefaultValueSql("randomblob(8)");
            conversation
                .Property(item => item.TeamsConversationId)
                .UseCollation("NOCASE");
            conversation
                .Property(item => item.CreatedAt)
                .HasConversion<long>()
                .HasColumnType("INTEGER");
            conversation
                .Property(item => item.UpdatedAt)
                .HasConversion<long>()
                .HasColumnType("INTEGER");

            modelBuilder.Entity<ApplicationAuditEventEntity>()
                .Property(item => item.OccurredAt)
                .HasConversion<long>()
                .HasColumnType("INTEGER");
            modelBuilder.Entity<ApplicationAuditEventEntity>()
                .Property(item => item.AuditMetadataJson)
                .HasColumnType("TEXT");

            var assignment =
                modelBuilder.Entity<UserDataAccessAssignmentEntity>();
            assignment.Property(item => item.CreatedAt)
                .HasConversion<long>().HasColumnType("INTEGER");
            assignment.Property(item => item.UpdatedAt)
                .HasConversion<long>().HasColumnType("INTEGER");
            assignment.Property(item => item.RowVersion)
                .HasColumnType("BLOB")
                .HasDefaultValueSql("randomblob(8)");
            modelBuilder.Entity<UserDataAccessRegionEntity>()
                .Property(item => item.RegionCode)
                .UseCollation("NOCASE");
            modelBuilder.Entity<UserDataAccessStoreEntity>()
                .Property(item => item.StoreId)
                .UseCollation("NOCASE");

            var outbox = modelBuilder.Entity<OutboxMessageEntity>();
            foreach (var propertyName in new[]
            {
                nameof(OutboxMessageEntity.OccurredAt),
                nameof(OutboxMessageEntity.NextAttemptAt),
                nameof(OutboxMessageEntity.CreatedAt),
                nameof(OutboxMessageEntity.UpdatedAt)
            })
            {
                outbox.Property<DateTimeOffset>(propertyName)
                    .HasConversion<long>().HasColumnType("INTEGER");
            }
            foreach (var propertyName in new[]
            {
                nameof(OutboxMessageEntity.PublishedAt),
                nameof(OutboxMessageEntity.DeadLetteredAt),
                nameof(OutboxMessageEntity.LockedUntil)
            })
            {
                outbox.Property<DateTimeOffset?>(propertyName)
                    .HasConversion<long?>().HasColumnType("INTEGER");
            }
            outbox.Property(item => item.PayloadJson).HasColumnType("TEXT");
            outbox.Property(item => item.RowVersion)
                .HasColumnType("BLOB")
                .HasDefaultValueSql("randomblob(8)");

            foreach (var entityType in new[]
            {
                typeof(TeamsNotificationTargetEntity),
                typeof(TeamsNotificationDeliveryEntity),
                typeof(TeamsCardActionSubmissionEntity)
            })
            {
                modelBuilder.Entity(entityType).Property("RowVersion")
                    .HasColumnType("BLOB")
                    .HasDefaultValueSql("randomblob(8)");
            }

            ConfigureSqliteDates<TeamsNotificationTargetEntity>(modelBuilder,
                "CreatedAt", "UpdatedAt");
            ConfigureSqliteDates<TeamsNotificationDeliveryEntity>(modelBuilder,
                "ReportUpdatedAt", "LockedUntil", "DeliveredAt",
                "CreatedAt", "UpdatedAt");
            ConfigureSqliteDates<TeamsCardActionSubmissionEntity>(modelBuilder,
                "LockedUntil", "CreatedAt", "UpdatedAt", "CompletedAt");
        }
    }

    private static void ConfigureSqliteDates<TEntity>(ModelBuilder modelBuilder,
        params string[] propertyNames) where TEntity : class
    {
        var entity = modelBuilder.Entity<TEntity>();
        foreach (var name in propertyNames)
        {
            var property = entity.Metadata.FindProperty(name)!;
            if (property.ClrType == typeof(DateTimeOffset?))
                entity.Property<DateTimeOffset?>(name).HasConversion<long?>()
                    .HasColumnType("INTEGER");
            else
                entity.Property<DateTimeOffset>(name).HasConversion<long>()
                    .HasColumnType("INTEGER");
        }
    }
}
