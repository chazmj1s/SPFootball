using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NCAA_Power_Ratings.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AvgScoreDeltas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Team1WinPct = table.Column<decimal>(type: "decimal(3,2)", nullable: false),
                    Team2WinPct = table.Column<decimal>(type: "decimal(3,2)", nullable: false),
                    AverageScoreDelta = table.Column<decimal>(type: "decimal(6,2)", nullable: false),
                    StDevP = table.Column<decimal>(type: "decimal(10,8)", nullable: false),
                    SampleSize = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AvgScoreDeltas", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Game",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Week = table.Column<int>(type: "int", nullable: false),
                    GameDate = table.Column<string>(type: "varchar(20)", nullable: true),
                    GameDay = table.Column<string>(type: "varchar(3)", nullable: true),
                    WinnerId = table.Column<int>(type: "int", nullable: false),
                    WinnerName = table.Column<string>(type: "varchar(50)", nullable: false),
                    WPoints = table.Column<int>(type: "int", nullable: false),
                    LoserId = table.Column<int>(type: "int", nullable: false),
                    LoserName = table.Column<string>(type: "varchar(50)", nullable: false),
                    LPoints = table.Column<int>(type: "int", nullable: false),
                    Location = table.Column<string>(type: "nvarchar(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Game", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MatchupHistory",
                columns: table => new
                {
                    Team1Id = table.Column<int>(type: "int", nullable: false),
                    Team2Id = table.Column<int>(type: "int", nullable: false),
                    GamesPlayed = table.Column<int>(type: "int", nullable: false),
                    AvgMargin = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    StDevMargin = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    UpsetRate = table.Column<decimal>(type: "decimal(4,3)", nullable: false),
                    LastPlayed = table.Column<int>(type: "int", nullable: false),
                    FirstPlayed = table.Column<int>(type: "int", nullable: false),
                    RivalryName = table.Column<string>(type: "varchar(100)", nullable: true),
                    RivalryTier = table.Column<string>(type: "varchar(20)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchupHistory", x => new { x.Team1Id, x.Team2Id });
                });

            migrationBuilder.CreateTable(
                name: "Team",
                columns: table => new
                {
                    TeamID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TeamName = table.Column<string>(type: "varchar(50)", nullable: false),
                    Alias = table.Column<string>(type: "varchar(50)", nullable: true),
                    Division = table.Column<string>(type: "varchar(20)", nullable: true),
                    Conference = table.Column<string>(type: "varchar(50)", nullable: true),
                    ConferenceAbbr = table.Column<string>(type: "varchar(10)", nullable: true),
                    ShortName = table.Column<string>(type: "varchar(20)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Team", x => x.TeamID);
                });

            migrationBuilder.CreateTable(
                name: "TeamRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TeamID = table.Column<int>(type: "int", nullable: false),
                    Year = table.Column<short>(type: "smallint", nullable: false),
                    Wins = table.Column<byte>(type: "tinyint", nullable: false),
                    Losses = table.Column<byte>(type: "tinyint", nullable: false),
                    PointsFor = table.Column<int>(type: "int", nullable: false),
                    PointsAgainst = table.Column<int>(type: "int", nullable: false),
                    BaseSOS = table.Column<decimal>(type: "decimal(10,3)", nullable: true),
                    SubSOS = table.Column<decimal>(type: "decimal(10,3)", nullable: true),
                    CombinedSOS = table.Column<decimal>(type: "decimal(10,4)", nullable: true),
                    PowerRating = table.Column<decimal>(type: "decimal(10,4)", nullable: true),
                    Ranking = table.Column<decimal>(type: "decimal(10,4)", nullable: true),
                    AvgPointsScored = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    AvgPointsAllowed = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    OffensiveZScore = table.Column<decimal>(type: "decimal(7,4)", nullable: false),
                    DefensiveZScore = table.Column<decimal>(type: "decimal(7,4)", nullable: false),
                    OffensiveRank = table.Column<int>(type: "int", nullable: false),
                    DefensiveRank = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamRecords_Team_TeamID",
                        column: x => x.TeamID,
                        principalTable: "Team",
                        principalColumn: "TeamID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WeeklyRankings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TeamID = table.Column<int>(type: "int", nullable: false),
                    Year = table.Column<short>(type: "smallint", nullable: false),
                    Week = table.Column<byte>(type: "tinyint", nullable: false),
                    Wins = table.Column<byte>(type: "tinyint", nullable: false),
                    Losses = table.Column<byte>(type: "tinyint", nullable: false),
                    PointsFor = table.Column<int>(type: "int", nullable: false),
                    PointsAgainst = table.Column<int>(type: "int", nullable: false),
                    BaseSOS = table.Column<decimal>(type: "decimal(10,3)", nullable: true),
                    SubSOS = table.Column<decimal>(type: "decimal(10,3)", nullable: true),
                    CombinedSOS = table.Column<decimal>(type: "decimal(10,4)", nullable: true),
                    PowerRating = table.Column<decimal>(type: "decimal(10,4)", nullable: true),
                    Ranking = table.Column<decimal>(type: "decimal(10,4)", nullable: true),
                    OverallRank = table.Column<int>(type: "int", nullable: false),
                    TierRank = table.Column<int>(type: "int", nullable: false),
                    AvgPointsScored = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    AvgPointsAllowed = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    OffensiveZScore = table.Column<decimal>(type: "decimal(7,4)", nullable: false),
                    DefensiveZScore = table.Column<decimal>(type: "decimal(7,4)", nullable: false),
                    OffensiveRank = table.Column<int>(type: "int", nullable: false),
                    DefensiveRank = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WeeklyRankings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WeeklyRankings_Team_TeamID",
                        column: x => x.TeamID,
                        principalTable: "Team",
                        principalColumn: "TeamID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TeamRecords_TeamID",
                table: "TeamRecords",
                column: "TeamID");

            migrationBuilder.CreateIndex(
                name: "IX_WeeklyRankings_TeamID_Year_Week",
                table: "WeeklyRankings",
                columns: new[] { "TeamID", "Year", "Week" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AvgScoreDeltas");

            migrationBuilder.DropTable(
                name: "Game");

            migrationBuilder.DropTable(
                name: "MatchupHistory");

            migrationBuilder.DropTable(
                name: "TeamRecords");

            migrationBuilder.DropTable(
                name: "WeeklyRankings");

            migrationBuilder.DropTable(
                name: "Team");
        }
    }
}
