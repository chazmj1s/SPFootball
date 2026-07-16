using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaturdayPulse.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddFollowedGameUpdate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_FollowedGames",
                table: "FollowedGames");

            migrationBuilder.RenameColumn(
                name: "GameId",
                table: "FollowedGames",
                newName: "Team2Id");

            migrationBuilder.AddColumn<int>(
                name: "Team1Id",
                table: "FollowedGames",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddPrimaryKey(
                name: "PK_FollowedGames",
                table: "FollowedGames",
                columns: new[] { "UserId", "Team1Id", "Team2Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_FollowedGames",
                table: "FollowedGames");

            migrationBuilder.DropColumn(
                name: "Team1Id",
                table: "FollowedGames");

            migrationBuilder.RenameColumn(
                name: "Team2Id",
                table: "FollowedGames",
                newName: "GameId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_FollowedGames",
                table: "FollowedGames",
                columns: new[] { "UserId", "GameId" });
        }
    }
}
