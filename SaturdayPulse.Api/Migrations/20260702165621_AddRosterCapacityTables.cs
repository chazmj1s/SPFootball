using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaturdayPulse.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRosterCapacityTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CoachRecords",
                columns: table => new
                {
                    Team = table.Column<string>(type: "TEXT", nullable: false),
                    Year = table.Column<int>(type: "INTEGER", nullable: false),
                    CoachName = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoachRecords", x => new { x.Team, x.Year });
                });

            migrationBuilder.CreateTable(
                name: "PlayerStats",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PlayerId = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Season = table.Column<int>(type: "INTEGER", nullable: false),
                    Team = table.Column<string>(type: "TEXT", nullable: false),
                    Position = table.Column<string>(type: "TEXT", nullable: true),
                    Category = table.Column<string>(type: "TEXT", nullable: false),
                    StatType = table.Column<string>(type: "TEXT", nullable: false),
                    StatValue = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerStats", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RosterPlayers",
                columns: table => new
                {
                    PlayerId = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Season = table.Column<int>(type: "INTEGER", nullable: false),
                    Team = table.Column<string>(type: "TEXT", nullable: false),
                    Position = table.Column<string>(type: "TEXT", nullable: true),
                    ClassYear = table.Column<int>(type: "INTEGER", nullable: true),
                    RecruitId = table.Column<string>(type: "TEXT", nullable: true),
                    RecruitRating = table.Column<double>(type: "REAL", nullable: true),
                    TransferRating = table.Column<double>(type: "REAL", nullable: true),
                    FirstName = table.Column<string>(type: "TEXT", nullable: true),
                    LastName = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RosterPlayers", x => new { x.PlayerId, x.Season });
                });

            migrationBuilder.CreateIndex(
                name: "IX_CoachRecords_Year",
                table: "CoachRecords",
                column: "Year");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerStats_PlayerId_Season",
                table: "PlayerStats",
                columns: new[] { "PlayerId", "Season" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerStats_Season",
                table: "PlayerStats",
                column: "Season");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerStats_Team",
                table: "PlayerStats",
                column: "Team");

            migrationBuilder.CreateIndex(
                name: "IX_RosterPlayers_Season",
                table: "RosterPlayers",
                column: "Season");

            migrationBuilder.CreateIndex(
                name: "IX_RosterPlayers_Team",
                table: "RosterPlayers",
                column: "Team");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CoachRecords");

            migrationBuilder.DropTable(
                name: "PlayerStats");

            migrationBuilder.DropTable(
                name: "RosterPlayers");
        }
    }
}
