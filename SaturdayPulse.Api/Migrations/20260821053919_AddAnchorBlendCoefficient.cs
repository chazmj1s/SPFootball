using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaturdayPulse.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAnchorBlendCoefficient : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AnchorBlendCoefficients",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Season = table.Column<int>(type: "INTEGER", nullable: false),
                    ComputedFromStartYear = table.Column<int>(type: "INTEGER", nullable: false),
                    ComputedThroughYear = table.Column<int>(type: "INTEGER", nullable: false),
                    WindowYears = table.Column<int>(type: "INTEGER", nullable: false),
                    ZRosterWeight = table.Column<decimal>(type: "decimal(10,4)", nullable: false),
                    RatingWeight = table.Column<decimal>(type: "decimal(10,4)", nullable: false),
                    ZRosterMean = table.Column<decimal>(type: "decimal(10,4)", nullable: false),
                    ZRosterStdDev = table.Column<decimal>(type: "decimal(10,4)", nullable: false),
                    TypicalPredictionErrorPoints = table.Column<decimal>(type: "decimal(10,4)", nullable: false),
                    GamesUsed = table.Column<int>(type: "INTEGER", nullable: false),
                    ComputedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnchorBlendCoefficients", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AnchorBlendCoefficients");
        }
    }
}
