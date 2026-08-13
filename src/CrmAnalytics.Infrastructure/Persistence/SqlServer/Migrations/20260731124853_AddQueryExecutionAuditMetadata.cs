using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrmAnalytics.Infrastructure.Persistence.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddQueryExecutionAuditMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DataSource",
                schema: "crm",
                table: "ApplicationAuditEvents",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "DurationMilliseconds",
                schema: "crm",
                table: "ApplicationAuditEvents",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ResultTruncated",
                schema: "crm",
                table: "ApplicationAuditEvents",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RowCount",
                schema: "crm",
                table: "ApplicationAuditEvents",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DataSource",
                schema: "crm",
                table: "ApplicationAuditEvents");

            migrationBuilder.DropColumn(
                name: "DurationMilliseconds",
                schema: "crm",
                table: "ApplicationAuditEvents");

            migrationBuilder.DropColumn(
                name: "ResultTruncated",
                schema: "crm",
                table: "ApplicationAuditEvents");

            migrationBuilder.DropColumn(
                name: "RowCount",
                schema: "crm",
                table: "ApplicationAuditEvents");
        }
    }
}
