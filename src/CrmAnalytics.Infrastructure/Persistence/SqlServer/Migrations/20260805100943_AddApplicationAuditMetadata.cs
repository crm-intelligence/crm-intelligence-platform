using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrmAnalytics.Infrastructure.Persistence.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddApplicationAuditMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AuditMetadataJson",
                schema: "crm",
                table: "ApplicationAuditEvents",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_ApplicationAuditEvents_AuditMetadataJson_IsJson",
                schema: "crm",
                table: "ApplicationAuditEvents",
                sql: "[AuditMetadataJson] IS NULL OR ISJSON([AuditMetadataJson]) = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ApplicationAuditEvents_AuditMetadataJson_IsJson",
                schema: "crm",
                table: "ApplicationAuditEvents");

            migrationBuilder.DropColumn(
                name: "AuditMetadataJson",
                schema: "crm",
                table: "ApplicationAuditEvents");
        }
    }
}
