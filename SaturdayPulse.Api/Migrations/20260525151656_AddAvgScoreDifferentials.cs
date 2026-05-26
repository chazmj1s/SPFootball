using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaturdayPulse.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAvgScoreDifferentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AvgScoreDifferentials",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StrengthDifferential = table.Column<decimal>(type: "TEXT", nullable: false),
                    AverageMargin = table.Column<decimal>(type: "TEXT", nullable: false),
                    StdDevMargin = table.Column<decimal>(type: "TEXT", nullable: false),
                    AverageTotalPoints = table.Column<decimal>(type: "TEXT", nullable: false),
                    SampleSize = table.Column<int>(type: "INTEGER", nullable: false),
                    LastUpdatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AvgScoreDifferentials", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AvgScoreDifferentials_StrengthDifferential",
                table: "AvgScoreDifferentials",
                column: "StrengthDifferential",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AvgScoreDifferentials");
        }
    }
}
