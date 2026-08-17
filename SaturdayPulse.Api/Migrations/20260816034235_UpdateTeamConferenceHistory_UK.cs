using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaturdayPulse.Api.Migrations
{
    /// <inheritdoc />
    public partial class UpdateTeamConferenceHistory_UK : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UQ_TeamsConferenceHistory_TeamId_StartYear",
                table: "TeamsConferenceHistory");

            migrationBuilder.CreateIndex(
                name: "UQ_TeamsConferenceHistory_TeamId_ConferenceId_StartYear",
                table: "TeamsConferenceHistory",
                columns: new[] { "TeamId", "ConferenceId", "StartYear" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UQ_TeamsConferenceHistory_TeamId_ConferenceId_StartYear",
                table: "TeamsConferenceHistory");

            migrationBuilder.CreateIndex(
                name: "UQ_TeamsConferenceHistory_TeamId_StartYear",
                table: "TeamsConferenceHistory",
                columns: new[] { "TeamId", "StartYear" },
                unique: true);
        }
    }
}
