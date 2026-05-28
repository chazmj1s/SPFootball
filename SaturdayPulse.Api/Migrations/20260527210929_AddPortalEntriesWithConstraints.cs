using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaturdayPulse.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPortalEntriesWithConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_PortalEntries_Destination",
                table: "PortalEntries",
                column: "Destination");

            migrationBuilder.CreateIndex(
                name: "IX_PortalEntries_Origin",
                table: "PortalEntries",
                column: "Origin");

            migrationBuilder.CreateIndex(
                name: "IX_PortalEntries_Season",
                table: "PortalEntries",
                column: "Season");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PortalEntries_Destination",
                table: "PortalEntries");

            migrationBuilder.DropIndex(
                name: "IX_PortalEntries_Origin",
                table: "PortalEntries");

            migrationBuilder.DropIndex(
                name: "IX_PortalEntries_Season",
                table: "PortalEntries");
        }
    }
}
