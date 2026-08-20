using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaturdayPulse.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRosterAdjustmentComposites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "PortalInComposite    ",
                table: "TeamRecords",
                type: "decimal(10,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PortalOutComposite   ",
                table: "TeamRecords",
                type: "decimal(10,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RecruitingComposite  ",
                table: "TeamRecords",
                type: "decimal(10,4)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PortalInComposite    ",
                table: "TeamRecords");

            migrationBuilder.DropColumn(
                name: "PortalOutComposite   ",
                table: "TeamRecords");

            migrationBuilder.DropColumn(
                name: "RecruitingComposite  ",
                table: "TeamRecords");
        }
    }
}
