using System;
using System.Collections.Generic;
using System.Linq;
using SaturdayPulse.Extensions;
using SaturdayPulse.Models;

namespace SaturdayPulse.Utilities
{
    /// <summary>
    /// Derives conference championship week from actual scheduled games rather
    /// than hardcoding a week number — CFBD's week numbering isn't guaranteed
    /// stable year to year (bye weeks, makeup games, etc. have shifted it
    /// before; see engineering notes).
    ///
    /// Rivalry Week is identified as whichever regular-season Week number has
    /// the most games landing in a Thu-Mon window starting Thanksgiving Day
    /// (the fourth Thursday of November). Championship Week is the week
    /// immediately following it.
    ///
    /// Returns null if that year's rivalry-week games aren't scheduled yet
    /// (season not live in CFBD) — deliberately no other fallback; the
    /// calling code decides what "not known yet" means for its own case.
    /// </summary>
    public static class ChampionshipWeekCalculator
    {
        public static int? GetChampionshipWeek(int year, IEnumerable<Games> games)
        {
            if (games == null) return null;

            var thanksgiving = FourthThursdayOfNovember(year);
            var windowEnd     = thanksgiving.AddDays(4); // Thu-Mon inclusive — covers Thu/Fri/Sat rivalry games

            var rivalryWeek = games
                .Where(g => string.Equals(g.SeasonType, "regular", StringComparison.OrdinalIgnoreCase)
                            && g.Year == year)
                .Select(g => (g.Week, Date: g.GameDate.ToDateTime()))
                .Where(x => x.Date.HasValue
                            && x.Date.Value.Date >= thanksgiving
                            && x.Date.Value.Date <= windowEnd)
                .GroupBy(x => x.Week)
                .OrderByDescending(grp => grp.Count())
                .Select(grp => (int?)grp.Key)
                .FirstOrDefault();

            return rivalryWeek.HasValue ? rivalryWeek.Value + 1 : null;
        }

        /// <summary>Thanksgiving Day for the given year — the fourth Thursday of November.</summary>
        private static DateTime FourthThursdayOfNovember(int year)
        {
            var firstOfNov = new DateTime(year, 11, 1);
            int offsetToFirstThursday = ((int)DayOfWeek.Thursday - (int)firstOfNov.DayOfWeek + 7) % 7;
            var firstThursday = firstOfNov.AddDays(offsetToFirstThursday);
            return firstThursday.AddDays(21); // +3 weeks = fourth Thursday
        }
    }
}
