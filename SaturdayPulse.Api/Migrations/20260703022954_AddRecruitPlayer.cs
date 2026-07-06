using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaturdayPulse.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRecruitPlayer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RecruitPlayers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    AthleteId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    RecruitType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Year = table.Column<int>(type: "INTEGER", nullable: false),
                    Ranking = table.Column<int>(type: "INTEGER", nullable: true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    School = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    CommittedTo = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Position = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Height = table.Column<double>(type: "REAL", nullable: true),
                    Weight = table.Column<int>(type: "INTEGER", nullable: true),
                    Stars = table.Column<int>(type: "INTEGER", nullable: false),
                    Rating = table.Column<double>(type: "REAL", nullable: false),
                    City = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    StateProvince = table.Column<string>(type: "TEXT", maxLength: 8, nullable: true),
                    Country = table.Column<string>(type: "TEXT", maxLength: 8, nullable: true),
                    Latitude = table.Column<double>(type: "REAL", nullable: true),
                    Longitude = table.Column<double>(type: "REAL", nullable: true),
                    FipsCode = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecruitPlayers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecruitPlayers_AthleteId_Year",
                table: "RecruitPlayers",
                columns: new[] { "AthleteId", "Year" });

            migrationBuilder.CreateIndex(
                name: "IX_RecruitPlayers_CommittedTo",
                table: "RecruitPlayers",
                column: "CommittedTo");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecruitPlayers");
        }
    }
}
