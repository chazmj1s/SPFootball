using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaturdayPulse.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAvgTotalPoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AvgTotalPoints",
                table: "MatchupHistory",
                type: "decimal(5,2)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AvgTotalPoints",
                table: "MatchupHistory");
        }
    }
}
