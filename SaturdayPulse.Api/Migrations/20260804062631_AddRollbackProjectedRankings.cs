using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaturdayPulse.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRollbackProjectedRankings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProjectedRankings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProjectedRankings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProjectedLosses = table.Column<int>(type: "INTEGER", nullable: false),
                    ProjectedPowerRating = table.Column<decimal>(type: "TEXT", nullable: true),
                    ProjectedSOS = table.Column<decimal>(type: "TEXT", nullable: true),
                    ProjectedWins = table.Column<int>(type: "INTEGER", nullable: false),
                    TeamId = table.Column<int>(type: "INTEGER", nullable: false),
                    Week = table.Column<byte>(type: "INTEGER", nullable: false),
                    Year = table.Column<short>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectedRankings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "UQ_ProjectedRanking_TeamId_Year_Week",
                table: "ProjectedRankings",
                columns: new[] { "TeamId", "Year", "Week" },
                unique: true);
        }
    }
}
