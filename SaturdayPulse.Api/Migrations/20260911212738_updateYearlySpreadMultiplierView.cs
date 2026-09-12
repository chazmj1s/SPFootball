using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaturdayPulse.Api.Migrations
{
    /// <inheritdoc />
    public partial class updateYearlySpreadMultiplierView : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW YearlySpreadMultiplier;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
            create view YearlySpreadMultiplier AS
                with YearlySpreads as(
                select g.year, avg(abs(g.homepoints - awaypoints))/avg(abs(trh.powerrating - tra.powerrating)) Spread
                from games g
                inner join teamrecords trh on g.homeid = trh.teamid and g.year = trh.year
                inner join teamrecords tra on g.awayid = tra.teamid and g.year = tra.year
                where g.homepoints is not null
                and g.awaypoints is not null
                group by g.year
                ),
                Timeline as( select distinct year from Games )
                select Timeline.year Targetyear, avg(spread) Multiplier
                from YearlySpreads
                inner join Timeline on YearlySpreads.year <= Timeline.year 
                group by Timeline.year
                order by Timeline.year
        ");
        }
    }
}
