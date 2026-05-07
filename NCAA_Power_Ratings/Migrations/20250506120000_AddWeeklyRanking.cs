using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NCAA_Power_Ratings.Migrations
{
    /// <inheritdoc />
    public partial class AddWeeklyRankings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WeeklyRankings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TeamID = table.Column<int>(type: "INTEGER", nullable: false),
                    Year = table.Column<short>(type: "smallint", nullable: false),
                    Week = table.Column<byte>(type: "tinyint", nullable: false),
                    Wins = table.Column<byte>(type: "tinyint", nullable: false),
                    Losses = table.Column<byte>(type: "tinyint", nullable: false),
                    PointsFor = table.Column<int>(type: "INTEGER", nullable: false),
                    PointsAgainst = table.Column<int>(type: "INTEGER", nullable: false),
                    BaseSOS = table.Column<decimal>(type: "decimal(10,3)", nullable: true),
                    SubSOS = table.Column<decimal>(type: "decimal(10,3)", nullable: true),
                    CombinedSOS = table.Column<decimal>(type: "decimal(10,4)", nullable: true),
                    PowerRating = table.Column<decimal>(type: "decimal(10,4)", nullable: true),
                    Ranking = table.Column<decimal>(type: "decimal(10,4)", nullable: true),
                    OverallRank = table.Column<int>(type: "INTEGER", nullable: false),
                    TierRank = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WeeklyRankings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WeeklyRankings_Teams_TeamID",
                        column: x => x.TeamID,
                        principalTable: "Teams",
                        principalColumn: "TeamID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WeeklyRankings_TeamID_Year_Week",
                table: "WeeklyRankings",
                columns: new[] { "TeamID", "Year", "Week" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "WeeklyRankings");
        }
    }
}
