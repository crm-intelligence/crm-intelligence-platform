using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrmAnalytics.Infrastructure.Persistence.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class InitialCrmPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "crm");

            migrationBuilder.CreateTable(
                name: "Conversations",
                schema: "crm",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    TeamsConversationId = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false, collation: "Latin1_General_100_CI_AS"),
                    UserId = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: false),
                    LastRequestId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Conversations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReportRequests",
                schema: "crm",
                columns: table => new
                {
                    RequestId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: false),
                    ConversationId = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    PreviousRequestId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    Prompt = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    ReferenceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ReportId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    PowerBiUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    Summary = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    ErrorCode = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ClarificationQuestion = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ClarificationResponse = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CanonicalRequestJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RejectionCode = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    RejectionMessage = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportRequests", x => x.RequestId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_LastRequestId",
                schema: "crm",
                table: "Conversations",
                column: "LastRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_TeamsConversationId",
                schema: "crm",
                table: "Conversations",
                column: "TeamsConversationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_TenantId_UserId_TeamsConversationId",
                schema: "crm",
                table: "Conversations",
                columns: new[] { "TenantId", "UserId", "TeamsConversationId" });

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_TenantId_UserId_UpdatedAt",
                schema: "crm",
                table: "Conversations",
                columns: new[] { "TenantId", "UserId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ReportRequests_ConversationId_CreatedAt",
                schema: "crm",
                table: "ReportRequests",
                columns: new[] { "ConversationId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ReportRequests_PreviousRequestId",
                schema: "crm",
                table: "ReportRequests",
                column: "PreviousRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_ReportRequests_Status_UpdatedAt",
                schema: "crm",
                table: "ReportRequests",
                columns: new[] { "Status", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ReportRequests_TenantId_UserId_ConversationId_CreatedAt",
                schema: "crm",
                table: "ReportRequests",
                columns: new[] { "TenantId", "UserId", "ConversationId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ReportRequests_TenantId_UserId_RequestId",
                schema: "crm",
                table: "ReportRequests",
                columns: new[] { "TenantId", "UserId", "RequestId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Conversations",
                schema: "crm");

            migrationBuilder.DropTable(
                name: "ReportRequests",
                schema: "crm");
        }
    }
}
