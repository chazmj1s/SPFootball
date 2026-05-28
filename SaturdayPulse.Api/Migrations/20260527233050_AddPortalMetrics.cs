using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaturdayPulse.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPortalMetrics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "RosterStrength",
                table: "WeeklyRankings",
                type: "decimal(10,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PortalDelta",
                table: "TeamRecords",
                type: "decimal(10,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RosterStrength",
                table: "TeamRecords",
                type: "decimal(10,4)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RosterStrength",
                table: "WeeklyRankings");

            migrationBuilder.DropColumn(
                name: "PortalDelta",
                table: "TeamRecords");

            migrationBuilder.DropColumn(
                name: "RosterStrength",
                table: "TeamRecords");
        }
    }
}
