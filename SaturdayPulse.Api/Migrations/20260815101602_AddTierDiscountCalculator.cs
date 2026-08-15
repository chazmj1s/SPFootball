using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaturdayPulse.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTierDiscountCalculator : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TierDiscountCoefficients",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Season = table.Column<int>(type: "INTEGER", nullable: false),
                    ComputedFromStartYear = table.Column<int>(type: "INTEGER", nullable: false),
                    ComputedThroughYear = table.Column<int>(type: "INTEGER", nullable: false),
                    WinDifferentialDiscount = table.Column<decimal>(type: "decimal(10,4)", nullable: false),
                    CaliberGapPoints = table.Column<decimal>(type: "decimal(10,4)", nullable: false),
                    TypicalPredictionErrorPoints = table.Column<decimal>(type: "decimal(10,4)", nullable: false),
                    GamesUsed = table.Column<int>(type: "INTEGER", nullable: false),
                    ComputedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TierDiscountCoefficients", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TierDiscountCoefficients");
        }
    }
}
