using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrmAnalytics.Infrastructure.Persistence.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddPolicyAssignmentsAndApplicationAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApplicationAuditEvents",
                schema: "crm",
                columns: table => new
                {
                    EventId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    RequestId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    PreviousRequestId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ActorUserId = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: true),
                    TenantId = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: true),
                    ReportStatus = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ReasonCode = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationAuditEvents", x => x.EventId);
                });

            migrationBuilder.CreateTable(
                name: "UserDataAccessAssignments",
                schema: "crm",
                columns: table => new
                {
                    TenantId = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: false),
                    AllowAllRegions = table.Column<bool>(type: "bit", nullable: false),
                    AllowAllStores = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserDataAccessAssignments", x => new { x.TenantId, x.UserId });
                });

            migrationBuilder.CreateTable(
                name: "UserDataAccessRegions",
                schema: "crm",
                columns: table => new
                {
                    TenantId = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: false),
                    RegionCode = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_CI_AS")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserDataAccessRegions", x => new { x.TenantId, x.UserId, x.RegionCode });
                    table.ForeignKey(
                        name: "FK_UserDataAccessRegions_UserDataAccessAssignments_TenantId_UserId",
                        columns: x => new { x.TenantId, x.UserId },
                        principalSchema: "crm",
                        principalTable: "UserDataAccessAssignments",
                        principalColumns: new[] { "TenantId", "UserId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserDataAccessStores",
                schema: "crm",
                columns: table => new
                {
                    TenantId = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: false),
                    StoreId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_CI_AS")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserDataAccessStores", x => new { x.TenantId, x.UserId, x.StoreId });
                    table.ForeignKey(
                        name: "FK_UserDataAccessStores_UserDataAccessAssignments_TenantId_UserId",
                        columns: x => new { x.TenantId, x.UserId },
                        principalSchema: "crm",
                        principalTable: "UserDataAccessAssignments",
                        principalColumns: new[] { "TenantId", "UserId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationAuditEvents_CorrelationId",
                schema: "crm",
                table: "ApplicationAuditEvents",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationAuditEvents_EventType_OccurredAt",
                schema: "crm",
                table: "ApplicationAuditEvents",
                columns: new[] { "EventType", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationAuditEvents_OccurredAt",
                schema: "crm",
                table: "ApplicationAuditEvents",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationAuditEvents_RequestId_OccurredAt",
                schema: "crm",
                table: "ApplicationAuditEvents",
                columns: new[] { "RequestId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationAuditEvents_TenantId_ActorUserId_OccurredAt",
                schema: "crm",
                table: "ApplicationAuditEvents",
                columns: new[] { "TenantId", "ActorUserId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_UserDataAccessAssignments_IsActive_TenantId",
                schema: "crm",
                table: "UserDataAccessAssignments",
                columns: new[] { "IsActive", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_UserDataAccessAssignments_UpdatedAt",
                schema: "crm",
                table: "UserDataAccessAssignments",
                column: "UpdatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApplicationAuditEvents",
                schema: "crm");

            migrationBuilder.DropTable(
                name: "UserDataAccessRegions",
                schema: "crm");

            migrationBuilder.DropTable(
                name: "UserDataAccessStores",
                schema: "crm");

            migrationBuilder.DropTable(
                name: "UserDataAccessAssignments",
                schema: "crm");
        }
    }
}
