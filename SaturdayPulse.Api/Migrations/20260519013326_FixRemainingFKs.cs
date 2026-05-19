using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaturdayPulse.Api.Migrations
{
    /// <inheritdoc />
    public partial class FixRemainingFKs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TeamConferenceHistory_Team_TeamID",
                table: "TeamConferenceHistory");

            migrationBuilder.AddForeignKey(
                name: "FK_TeamConferenceHistory_Teams_TeamID",
                table: "TeamConferenceHistory",
                column: "TeamID",
                principalTable: "Teams",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TeamConferenceHistory_Teams_TeamID",
                table: "TeamConferenceHistory");

            migrationBuilder.AddForeignKey(
                name: "FK_TeamConferenceHistory_Team_TeamID",
                table: "TeamConferenceHistory",
                column: "TeamID",
                principalTable: "Team",
                principalColumn: "TeamID",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
