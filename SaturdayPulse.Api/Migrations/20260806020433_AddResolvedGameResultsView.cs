using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaturdayPulse.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddResolvedGameResultsView : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
            CREATE VIEW ResolvedGameResults AS
            SELECT
                g.GameId, g.Year, g.Week, g.HomeId, g.AwayId,
                g.HomePoints, g.AwayPoints, 0 AS IsProjected
            FROM Games g
            WHERE NOT (COALESCE(g.HomePoints, 0) = 0 AND COALESCE(g.AwayPoints, 0) = 0)
            UNION ALL
            SELECT
                p.GameId, p.Year, p.Week, g2.HomeId, g2.AwayId,
                p.HomePoints, p.AwayPoints, 1 AS IsProjected
            FROM Projections p
            JOIN Games g2 ON g2.GameId = p.GameId
            WHERE COALESCE(g2.HomePoints, 0) = 0 AND COALESCE(g2.AwayPoints, 0) = 0;
        ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW ResolvedGameResults;");
        }
    }
}
