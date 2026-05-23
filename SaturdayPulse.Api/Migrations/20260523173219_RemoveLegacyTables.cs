using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaturdayPulse.Api.Migrations
{
    /// <inheritdoc />
    public partial class RemoveLegacyTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Game",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    GameDate = table.Column<string>(type: "varchar(20)", nullable: true),
                    GameDay = table.Column<string>(type: "varchar(3)", nullable: true),
                    LPoints = table.Column<int>(type: "INTEGER", nullable: false),
                    Location = table.Column<char>(type: "TEXT", nullable: false),
                    LoserId = table.Column<int>(type: "INTEGER", nullable: false),
                    LoserName = table.Column<string>(type: "varchar(50)", nullable: false),
                    WPoints = table.Column<int>(type: "INTEGER", nullable: false),
                    Week = table.Column<int>(type: "INTEGER", nullable: false),
                    WinnerId = table.Column<int>(type: "INTEGER", nullable: false),
                    WinnerName = table.Column<string>(type: "varchar(50)", nullable: false),
                    Year = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Game", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Team",
                columns: table => new
                {
                    TeamID = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Alias = table.Column<string>(type: "varchar(50)", nullable: true),
                    Conference = table.Column<string>(type: "varchar(50)", nullable: true),
                    ConferenceAbbr = table.Column<string>(type: "varchar(15)", nullable: true),
                    Division = table.Column<string>(type: "varchar(20)", nullable: true),
                    ShortName = table.Column<string>(type: "varchar(20)", nullable: true),
                    TeamName = table.Column<string>(type: "varchar(50)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Team", x => x.TeamID);
                });

            migrationBuilder.CreateTable(
                name: "TeamConferenceHistory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TeamID = table.Column<int>(type: "INTEGER", nullable: false),
                    Conference = table.Column<string>(type: "varchar(50)", nullable: false),
                    ConferenceAbbr = table.Column<string>(type: "varchar(15)", nullable: true),
                    EndYear = table.Column<int>(type: "INTEGER", nullable: true),
                    StartYear = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamConferenceHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamConferenceHistory_Teams_TeamID",
                        column: x => x.TeamID,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TeamConferenceHistory_TeamID_EndYear",
                table: "TeamConferenceHistory",
                columns: new[] { "TeamID", "EndYear" });

            migrationBuilder.CreateIndex(
                name: "IX_TeamConferenceHistory_TeamID_StartYear",
                table: "TeamConferenceHistory",
                columns: new[] { "TeamID", "StartYear" },
                unique: true);
        }
    }
}
