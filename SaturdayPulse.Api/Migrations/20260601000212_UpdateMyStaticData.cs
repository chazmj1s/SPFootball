using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaturdayPulse.Api.Migrations
{
    /// <inheritdoc />
    public partial class UpdateMyStaticData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("update Games \r\nset seasontype = 'playoff'\r\nwhere seasonType = 'postseason'\r\nand year = 2025\r\nand (homename in ('Indiana','Ohio State','Georgia','Texas Tech','Oregon','Ole Miss','Texas A&M','Oklahoma','Alabama','Miami','Tulane','James Madison')\r\nor awayname in('Indiana','Ohio State','Georgia','Texas Tech','Oregon','Ole Miss','Texas A&M','Oklahoma','Alabama','Miami','Tulane','James Madison'))\r\n");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
