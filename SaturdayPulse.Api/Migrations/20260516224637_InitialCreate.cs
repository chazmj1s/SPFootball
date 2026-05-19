using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaturdayPulse.Api.Migrations
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
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    Team1WinPct = table.Column<decimal>(type: "decimal(3,2)", nullable: false),
                    Team2WinPct = table.Column<decimal>(type: "decimal(3,2)", nullable: false),
                    AverageScoreDelta = table.Column<decimal>(type: "decimal(6,2)", nullable: false),
                    StDevP = table.Column<decimal>(type: "decimal(10,8)", nullable: false),
                    SampleSize = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AvgScoreDeltas", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Conferences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    ConferenceId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    ShortName = table.Column<string>(type: "TEXT", nullable: true),
                    Abbreviation = table.Column<string>(type: "TEXT", nullable: true),
                    Classification = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Conferences", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Game",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    Year = table.Column<int>(type: "INTEGER", nullable: false),
                    Week = table.Column<int>(type: "INTEGER", nullable: false),
                    GameDate = table.Column<string>(type: "varchar(20)", nullable: true),
                    GameDay = table.Column<string>(type: "varchar(3)", nullable: true),
                    WinnerId = table.Column<int>(type: "INTEGER", nullable: false),
                    WinnerName = table.Column<string>(type: "varchar(50)", nullable: false),
                    WPoints = table.Column<int>(type: "INTEGER", nullable: false),
                    LoserId = table.Column<int>(type: "INTEGER", nullable: false),
                    LoserName = table.Column<string>(type: "varchar(50)", nullable: false),
                    LPoints = table.Column<int>(type: "INTEGER", nullable: false),
                    Location = table.Column<char>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Game", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Games",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    GameId = table.Column<int>(type: "INTEGER", nullable: false),
                    Year = table.Column<int>(type: "INTEGER", nullable: false),
                    Week = table.Column<int>(type: "INTEGER", nullable: false),
                    SeasonType = table.Column<string>(type: "TEXT", nullable: false),
                    GameDate = table.Column<string>(type: "TEXT", nullable: true),
                    GameDay = table.Column<string>(type: "TEXT", nullable: true),
                    HomeId = table.Column<int>(type: "INTEGER", nullable: true),
                    HomeName = table.Column<string>(type: "TEXT", nullable: true),
                    HomePoints = table.Column<int>(type: "INTEGER", nullable: true),
                    AwayId = table.Column<int>(type: "INTEGER", nullable: true),
                    AwayName = table.Column<string>(type: "TEXT", nullable: true),
                    AwayPoints = table.Column<int>(type: "INTEGER", nullable: true),
                    NeutralSite = table.Column<bool>(type: "INTEGER", nullable: false),
                    ConferenceGame = table.Column<bool>(type: "INTEGER", nullable: false),
                    Attendance = table.Column<int>(type: "INTEGER", nullable: true),
                    Venue = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Games", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Lines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    GameId = table.Column<int>(type: "INTEGER", nullable: false),
                    Provider = table.Column<string>(type: "TEXT", nullable: false),
                    Spread = table.Column<decimal>(type: "TEXT", nullable: true),
                    SpreadOpen = table.Column<decimal>(type: "TEXT", nullable: true),
                    FormattedSpread = table.Column<string>(type: "TEXT", nullable: true),
                    OverUnder = table.Column<decimal>(type: "TEXT", nullable: true),
                    OverUnderOpen = table.Column<decimal>(type: "TEXT", nullable: true),
                    HomeMoneyline = table.Column<int>(type: "INTEGER", nullable: true),
                    AwayMoneyline = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Lines", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MatchupHistory",
                columns: table => new
                {
                    Team1Id = table.Column<int>(type: "INTEGER", nullable: false),
                    Team2Id = table.Column<int>(type: "INTEGER", nullable: false),
                    GamesPlayed = table.Column<int>(type: "INTEGER", nullable: false),
                    AvgMargin = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    StDevMargin = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    UpsetRate = table.Column<decimal>(type: "decimal(4,3)", nullable: false),
                    LastPlayed = table.Column<int>(type: "INTEGER", nullable: false),
                    FirstPlayed = table.Column<int>(type: "INTEGER", nullable: false),
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
                    TeamID = table.Column<int>(type: "INTEGER", nullable: false),
                    TeamName = table.Column<string>(type: "varchar(50)", nullable: false),
                    Alias = table.Column<string>(type: "varchar(50)", nullable: true),
                    Division = table.Column<string>(type: "varchar(20)", nullable: true),
                    Conference = table.Column<string>(type: "varchar(50)", nullable: true),
                    ConferenceAbbr = table.Column<string>(type: "varchar(15)", nullable: true),
                    ShortName = table.Column<string>(type: "varchar(20)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Team", x => x.TeamID);
                });

            migrationBuilder.CreateTable(
                name: "Teams",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    TeamId = table.Column<int>(type: "INTEGER", nullable: false),
                    TeamName = table.Column<string>(type: "TEXT", nullable: false),
                    Mascot = table.Column<string>(type: "TEXT", nullable: true),
                    Abbreviation = table.Column<string>(type: "TEXT", nullable: true),
                    Alias = table.Column<string>(type: "TEXT", nullable: true),
                    Division = table.Column<string>(type: "TEXT", nullable: true),
                    ConferenceId = table.Column<int>(type: "INTEGER", nullable: true),
                    ShortName = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Teams", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TeamsConferenceHistory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    TeamId = table.Column<int>(type: "INTEGER", nullable: false),
                    ConferenceId = table.Column<int>(type: "INTEGER", nullable: false),
                    StartYear = table.Column<int>(type: "INTEGER", nullable: false),
                    EndYear = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamsConferenceHistory", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TeamConferenceHistory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    TeamID = table.Column<int>(type: "INTEGER", nullable: false),
                    Conference = table.Column<string>(type: "varchar(50)", nullable: false),
                    ConferenceAbbr = table.Column<string>(type: "varchar(15)", nullable: true),
                    StartYear = table.Column<int>(type: "INTEGER", nullable: false),
                    EndYear = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamConferenceHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamConferenceHistory_Team_TeamID",
                        column: x => x.TeamID,
                        principalTable: "Team",
                        principalColumn: "TeamID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TeamRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    TeamID = table.Column<int>(type: "INTEGER", nullable: false),
                    Year = table.Column<short>(type: "smallint", nullable: false),
                    Wins = table.Column<byte>(type: "smallint", nullable: false),
                    Losses = table.Column<byte>(type: "smallint", nullable: false),
                    PointsFor = table.Column<int>(type: "INTEGER", nullable: false),
                    PointsAgainst = table.Column<int>(type: "INTEGER", nullable: false),
                    BaseSOS = table.Column<decimal>(type: "decimal(10,3)", nullable: true),
                    SubSOS = table.Column<decimal>(type: "decimal(10,3)", nullable: true),
                    CombinedSOS = table.Column<decimal>(type: "decimal(10,4)", nullable: true),
                    PowerRating = table.Column<decimal>(type: "decimal(10,4)", nullable: true),
                    Ranking = table.Column<decimal>(type: "decimal(10,4)", nullable: true),
                    AvgPointsScored = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    AvgPointsAllowed = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    OffensiveZScore = table.Column<decimal>(type: "decimal(7,4)", nullable: false),
                    DefensiveZScore = table.Column<decimal>(type: "decimal(7,4)", nullable: false),
                    OffensiveRank = table.Column<int>(type: "INTEGER", nullable: false),
                    DefensiveRank = table.Column<int>(type: "INTEGER", nullable: false),
                    SeedRating = table.Column<decimal>(type: "decimal(10,4)", nullable: true),
                    TrendRating = table.Column<decimal>(type: "decimal(10,4)", nullable: true),
                    PedigreeRating = table.Column<decimal>(type: "decimal(10,4)", nullable: true)
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
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    TeamID = table.Column<int>(type: "INTEGER", nullable: false),
                    Year = table.Column<short>(type: "smallint", nullable: false),
                    Week = table.Column<byte>(type: "smallint", nullable: false),
                    Wins = table.Column<byte>(type: "smallint", nullable: false),
                    Losses = table.Column<byte>(type: "smallint", nullable: false),
                    PointsFor = table.Column<int>(type: "INTEGER", nullable: false),
                    PointsAgainst = table.Column<int>(type: "INTEGER", nullable: false),
                    BaseSOS = table.Column<decimal>(type: "decimal(10,3)", nullable: true),
                    SubSOS = table.Column<decimal>(type: "decimal(10,3)", nullable: true),
                    CombinedSOS = table.Column<decimal>(type: "decimal(10,4)", nullable: true),
                    PowerRating = table.Column<decimal>(type: "decimal(10,4)", nullable: true),
                    Ranking = table.Column<decimal>(type: "decimal(10,4)", nullable: true),
                    OverallRank = table.Column<int>(type: "INTEGER", nullable: false),
                    TierRank = table.Column<int>(type: "INTEGER", nullable: false),
                    AvgPointsScored = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    AvgPointsAllowed = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    OffensiveZScore = table.Column<decimal>(type: "decimal(7,4)", nullable: false),
                    DefensiveZScore = table.Column<decimal>(type: "decimal(7,4)", nullable: false),
                    OffensiveRank = table.Column<int>(type: "INTEGER", nullable: false),
                    DefensiveRank = table.Column<int>(type: "INTEGER", nullable: false)
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
                name: "UQ_Conferences_ConferenceId",
                table: "Conferences",
                column: "ConferenceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Games_Year_Week",
                table: "Games",
                columns: new[] { "Year", "Week" });

            migrationBuilder.CreateIndex(
                name: "UQ_Games_GameId",
                table: "Games",
                column: "GameId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamConferenceHistory_TeamID_EndYear",
                table: "TeamConferenceHistory",
                columns: new[] { "TeamID", "EndYear" });

            migrationBuilder.CreateIndex(
                name: "IX_TeamConferenceHistory_TeamID_StartYear",
                table: "TeamConferenceHistory",
                columns: new[] { "TeamID", "StartYear" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamRecords_TeamID",
                table: "TeamRecords",
                column: "TeamID");

            migrationBuilder.CreateIndex(
                name: "UQ_Teams_TeamId",
                table: "Teams",
                column: "TeamId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_TeamsConferenceHistory_TeamId_StartYear",
                table: "TeamsConferenceHistory",
                columns: new[] { "TeamId", "StartYear" },
                unique: true);

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
                name: "Conferences");

            migrationBuilder.DropTable(
                name: "Game");

            migrationBuilder.DropTable(
                name: "Games");

            migrationBuilder.DropTable(
                name: "Lines");

            migrationBuilder.DropTable(
                name: "MatchupHistory");

            migrationBuilder.DropTable(
                name: "TeamConferenceHistory");

            migrationBuilder.DropTable(
                name: "TeamRecords");

            migrationBuilder.DropTable(
                name: "Teams");

            migrationBuilder.DropTable(
                name: "TeamsConferenceHistory");

            migrationBuilder.DropTable(
                name: "WeeklyRankings");

            migrationBuilder.DropTable(
                name: "Team");
        }
    }
}
