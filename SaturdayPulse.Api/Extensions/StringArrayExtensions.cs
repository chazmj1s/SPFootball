using SaturdayPulse.Models;
using SaturdayPulse.Utilities;
using System.Text.RegularExpressions;

namespace SaturdayPulse.Extensions
{
    public static class StringArrayExtensions
    {
        /// <summary>
        /// Converts a string array of game data fields into a Game object.
        /// </summary>
        /// <param name="cells">Array of game data fields</param>
        /// <param name="yearIn">Year as string for ID generation</param>
        /// <param name="teams">Collection of teams for ID lookup</param>
        /// <returns>A new Game object populated from the array data</returns>
        public static Game ToGame(this string[] cells, string yearIn, IEnumerable<Team> teams)
        {
            var regex = @"\(\d+\)\s*";
            var year = int.TryParse(yearIn, out int x) ? x : 0;
            var xIdx = -1; // Set negative Ids for missing teams
            var map = ColumnMap.ForYear(year);

            // Extract winnerName and loserName from the appropriate cells
            var winnerName = Regex.Replace(cells[map.WinnerName], regex, "").Trim();
            var loserName = Regex.Replace(cells[map.LoserName], regex, "").Trim();

            // Find team IDs by name or alias
            int winnerId = teams.FirstOrDefault(t => winnerName.Equals(t.TeamName.Trim()) ||
                                         (t.Alias != null && t.Alias.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                           .Any(a => winnerName.Equals(a, StringComparison.OrdinalIgnoreCase))))?.TeamID ?? xIdx;

            int loserId = teams.FirstOrDefault(t => loserName.Equals(t.TeamName.Trim()) ||
                                        (t.Alias != null && t.Alias.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                           .Any(a => loserName.Equals(a, StringComparison.OrdinalIgnoreCase))))?.TeamID ?? xIdx;

            // Determine location
            var siteCellText = cells[map.Location].Trim();
            char siteIndicator = siteCellText.Contains('@') ? 'L' : siteCellText.Contains('N') ? 'N' : 'W';

            // Parse date — stored as-is from file e.g. "Aug 23 2025"
            var gameDate = cells.Length > map.Date
                ? cells[map.Date].Trim()
                : null;

            // Parse day — e.g. "Sat", "Fri", "Thu"
            var gameDay = cells.Length > map.Day
                ? cells[map.Day].Trim()
                : null;

            // Create and return the Game object
            return new Game
            {
                Id = int.TryParse(yearIn + cells[map.RowId].Trim(), out int id) ? id : 0,
                Week = int.TryParse(cells[map.Week].Trim(), out int week) ? week : 0,
                GameDate = gameDate,
                GameDay = gameDay,
                WinnerId = winnerId,
                WinnerName = winnerName,
                WPoints = int.TryParse(cells[map.WPoints].Trim(), out int wpoints) ? wpoints : 0,
                Location = siteIndicator,
                LoserId = loserId,
                LoserName = loserName,
                LPoints = int.TryParse(cells[map.LPoints].Trim(), out int lpoints) ? lpoints : 0,
                Year = year
            };
        }
    }
}