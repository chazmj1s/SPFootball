using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaturdayPulse.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPlayerStatUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_RosterPlayers",
                table: "RosterPlayers");

            migrationBuilder.AddPrimaryKey(
                name: "PK_RosterPlayers",
                table: "RosterPlayers",
                columns: new[] { "PlayerId", "Season", "Team" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_RosterPlayers",
                table: "RosterPlayers");

            migrationBuilder.AddPrimaryKey(
                name: "PK_RosterPlayers",
                table: "RosterPlayers",
                columns: new[] { "PlayerId", "Season" });
        }
    }
}
