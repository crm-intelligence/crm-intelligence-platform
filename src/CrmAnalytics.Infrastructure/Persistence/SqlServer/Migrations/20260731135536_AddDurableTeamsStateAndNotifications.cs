using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrmAnalytics.Infrastructure.Persistence.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddDurableTeamsStateAndNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TeamsCardActionSubmissions",
                schema: "crm",
                columns: table => new
                {
                    ActionToken = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RequestId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ActionType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    State = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ResultRequestId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    LockOwner = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    LockedUntil = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamsCardActionSubmissions", x => x.ActionToken);
                });

            migrationBuilder.CreateTable(
                name: "TeamsNotificationDeliveries",
                schema: "crm",
                columns: table => new
                {
                    DeliveryId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RequestId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    NotificationStatus = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ReportUpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    State = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    LockOwner = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    LockedUntil = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                    DeliveredAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamsNotificationDeliveries", x => x.DeliveryId);
                });

            migrationBuilder.CreateTable(
                name: "TeamsNotificationTargets",
                schema: "crm",
                columns: table => new
                {
                    RequestId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ConversationId = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamsNotificationTargets", x => x.RequestId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TeamsNotificationDeliveries_RequestId_ReportUpdatedAt",
                schema: "crm",
                table: "TeamsNotificationDeliveries",
                columns: new[] { "RequestId", "ReportUpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TeamsNotificationDeliveries_State_LockedUntil",
                schema: "crm",
                table: "TeamsNotificationDeliveries",
                columns: new[] { "State", "LockedUntil" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TeamsCardActionSubmissions",
                schema: "crm");

            migrationBuilder.DropTable(
                name: "TeamsNotificationDeliveries",
                schema: "crm");

            migrationBuilder.DropTable(
                name: "TeamsNotificationTargets",
                schema: "crm");
        }
    }
}
