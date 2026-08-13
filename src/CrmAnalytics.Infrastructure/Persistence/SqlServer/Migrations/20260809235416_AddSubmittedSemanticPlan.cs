using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrmAnalytics.Infrastructure.Persistence.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddSubmittedSemanticPlan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SemanticPlanJson",
                schema: "crm",
                table: "ReportRequests",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SemanticPlanJson",
                schema: "crm",
                table: "ReportRequests");
        }
    }
}
