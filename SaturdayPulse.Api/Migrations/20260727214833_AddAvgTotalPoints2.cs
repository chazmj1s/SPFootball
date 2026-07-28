using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaturdayPulse.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAvgTotalPoints2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "AvgTotalPoints",
                table: "MatchupHistory",
                type: "decimal(6,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(5,2)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "AvgTotalPoints",
                table: "MatchupHistory",
                type: "decimal(5,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(6,2)");
        }
    }
}
