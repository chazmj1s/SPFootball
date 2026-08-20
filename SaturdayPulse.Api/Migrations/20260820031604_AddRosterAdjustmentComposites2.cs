using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaturdayPulse.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRosterAdjustmentComposites2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "RecruitingComposite  ",
                table: "TeamRecords",
                newName: "RecruitingComposite");

            migrationBuilder.RenameColumn(
                name: "PortalOutComposite   ",
                table: "TeamRecords",
                newName: "PortalOutComposite");

            migrationBuilder.RenameColumn(
                name: "PortalInComposite    ",
                table: "TeamRecords",
                newName: "PortalInComposite");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "RecruitingComposite",
                table: "TeamRecords",
                newName: "RecruitingComposite  ");

            migrationBuilder.RenameColumn(
                name: "PortalOutComposite",
                table: "TeamRecords",
                newName: "PortalOutComposite   ");

            migrationBuilder.RenameColumn(
                name: "PortalInComposite",
                table: "TeamRecords",
                newName: "PortalInComposite    ");
        }
    }
}
