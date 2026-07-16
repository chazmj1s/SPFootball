using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaturdayPulse.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddUserProfileAndContactInfo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FollowedGames",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    GameId = table.Column<int>(type: "INTEGER", nullable: false),
                    FollowedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsSynced = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FollowedGames", x => new { x.UserId, x.GameId });
                });

            migrationBuilder.CreateTable(
                name: "FollowedTeams",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    TeamId = table.Column<int>(type: "INTEGER", nullable: false),
                    FollowedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsSynced = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FollowedTeams", x => new { x.UserId, x.TeamId });
                });

            migrationBuilder.CreateTable(
                name: "UserProfiles",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Handle = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false, collation: "NOCASE"),
                    HandleChangedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PrimaryTeamId = table.Column<int>(type: "INTEGER", nullable: true),
                    ExpiryDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsSynced = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserProfiles", x => x.UserId);
                });

            migrationBuilder.CreateTable(
                name: "UserContactInfos",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Email = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    EmailVerifiedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PhoneNumber = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    PhoneVerifiedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    MarketingEmailConsent = table.Column<bool>(type: "INTEGER", nullable: false),
                    MarketingEmailConsentAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    MarketingSmsConsent = table.Column<bool>(type: "INTEGER", nullable: false),
                    MarketingSmsConsentAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    MarketingSmsConsentSource = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsSynced = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserContactInfos", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_UserContactInfos_UserProfiles_UserId",
                        column: x => x.UserId,
                        principalTable: "UserProfiles",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "UQ_UserContactInfo_Email",
                table: "UserContactInfos",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_UserProfile_Handle",
                table: "UserProfiles",
                column: "Handle",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FollowedGames");

            migrationBuilder.DropTable(
                name: "FollowedTeams");

            migrationBuilder.DropTable(
                name: "UserContactInfos");

            migrationBuilder.DropTable(
                name: "UserProfiles");
        }
    }
}
