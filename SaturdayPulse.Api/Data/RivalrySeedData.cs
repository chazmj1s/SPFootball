// Rivalry seed data based on historical analysis
// Source: D1 FBS Top 50 Rivalries spreadsheet
// Tier meanings:
//   EPIC: Century+ history, anything can happen, highest variance
//   NATIONAL: Major cross-regional rivalries, significant variance
//   STATE: In-state or regional rivalries, moderate to high variance
//   MEH: Lower-tier rivalries with regional significance but limited national appeal

using SaturdayPulse.Models;

namespace SaturdayPulse.Data
{
    public static class RivalrySeedData
    {
        public static List<RivalryMetadata> GetRivalries()
        {
            return new List<RivalryMetadata>
            {
                // EPIC TIER - Expected variance ratio: 1.75x
                new() { Team1Name = "Ohio State", Team2Name = "Michigan", RivalryName = "The Game", Tier = "EPIC", SeriesAge = 128 },
                new() { Team1Name = "Alabama", Team2Name = "Auburn", RivalryName = "Iron Bowl", Tier = "EPIC", SeriesAge = 132 },
                new() { Team1Name = "Texas", Team2Name = "Oklahoma", RivalryName = "Red River Shootout", Tier = "EPIC", SeriesAge = 125 },

                // NATIONAL TIER - Expected variance ratio: 1.5x
                new() { Team1Name = "Army", Team2Name = "Navy", RivalryName = "125 meetings, never permanently cancelled", Tier = "NATIONAL", SeriesAge = 135 },
                new() { Team1Name = "LSU", Team2Name = "Alabama", RivalryName = "Third Saturday in November", Tier = "NATIONAL", SeriesAge = 130 },
                new() { Team1Name = "Tennessee", Team2Name = "Alabama", RivalryName = "Third Saturday in October", Tier = "NATIONAL", SeriesAge = 124 },
                new() { Team1Name = "Florida", Team2Name = "Georgia", RivalryName = "World's Largest Outdoor Cocktail Party", Tier = "NATIONAL", SeriesAge = 121 },
                new() { Team1Name = "Notre Dame", Team2Name = "USC", RivalryName = "Classic intersectional since 1926", Tier = "NATIONAL", SeriesAge = 99 },
                new() { Team1Name = "Penn State", Team2Name = "Ohio State", RivalryName = "Whiteout vs Horseshoe", Tier = "NATIONAL", SeriesAge = 113 },
                new() { Team1Name = "Miami", Team2Name = "Florida State", RivalryName = "Wide Right I, II, III", Tier = "NATIONAL", SeriesAge = 74 },
                new() { Team1Name = "Florida", Team2Name = "Florida State", RivalryName = "Sunshine Showdown", Tier = "NATIONAL", SeriesAge = 67 },

                // STATE TIER - Expected variance ratio: 1.3x
                new() { Team1Name = "Wisconsin", Team2Name = "Minnesota", RivalryName = "Most-played FBS series ever", Tier = "STATE", SeriesAge = 135 },
                new() { Team1Name = "Minnesota", Team2Name = "Iowa", RivalryName = "Floyd of Rosedale (bronze pig)", Tier = "STATE", SeriesAge = 134 },
                new() { Team1Name = "Auburn", Team2Name = "Georgia", RivalryName = "Deep South's Oldest Rivalry", Tier = "STATE", SeriesAge = 133 },
                new() { Team1Name = "Stanford", Team2Name = "California", RivalryName = "The Big Game", Tier = "STATE", SeriesAge = 133 },
                new() { Team1Name = "Mississippi", Team2Name = "Mississippi State", RivalryName = "Egg Bowl", Tier = "STATE", SeriesAge = 124 },
                new() { Team1Name = "Oregon", Team2Name = "Oregon State", RivalryName = "Civil War (renamed)", Tier = "STATE", SeriesAge = 131 },
                new() { Team1Name = "Clemson", Team2Name = "South Carolina", RivalryName = "Palmetto Bowl", Tier = "STATE", SeriesAge = 129 },
                new() { Team1Name = "Texas", Team2Name = "Texas A&M", RivalryName = "118 meetings", Tier = "STATE", SeriesAge = 131 },
                new() { Team1Name = "Oklahoma", Team2Name = "Oklahoma State", RivalryName = "Bedlam", Tier = "STATE", SeriesAge = 121 },
                new() { Team1Name = "Washington", Team2Name = "Washington State", RivalryName = "Apple Cup", Tier = "STATE", SeriesAge = 125 },
                new() { Team1Name = "Michigan", Team2Name = "Michigan State", RivalryName = "Paul Bunyan Trophy", Tier = "STATE", SeriesAge = 127 },
                new() { Team1Name = "Georgia", Team2Name = "Georgia Tech", RivalryName = "Clean, Old-Fashioned Hate", Tier = "STATE", SeriesAge = 132 },
                new() { Team1Name = "North Carolina", Team2Name = "NC State", RivalryName = "Tobacco Road rivalry", Tier = "STATE", SeriesAge = 131 },
                new() { Team1Name = "Utah", Team2Name = "BYU", RivalryName = "Holy War", Tier = "STATE", SeriesAge = 129 },
                new() { Team1Name = "Arizona", Team2Name = "Arizona State", RivalryName = "Territorial Cup", Tier = "STATE", SeriesAge = 126 },
                new() { Team1Name = "Virginia", Team2Name = "Virginia Tech", RivalryName = "Commonwealth Clash", Tier = "STATE", SeriesAge = 130 },
                new() { Team1Name = "Pittsburgh", Team2Name = "Penn State", RivalryName = "Keystone State rivalry", Tier = "STATE", SeriesAge = 132 },
                new() { Team1Name = "UCLA", Team2Name = "USC", RivalryName = "Crosstown Showdown", Tier = "STATE", SeriesAge = 96 },
                new() { Team1Name = "Colorado", Team2Name = "Colorado State", RivalryName = "Rocky Mountain Showdown", Tier = "STATE", SeriesAge = 132 },
                new() { Team1Name = "LSU", Team2Name = "Ole Miss", RivalryName = "Annual SEC West showdown", Tier = "STATE", SeriesAge = 131 },
                new() { Team1Name = "Iowa", Team2Name = "Iowa State", RivalryName = "Cy-Hawk Trophy", Tier = "STATE", SeriesAge = 131 },
                new() { Team1Name = "Arkansas", Team2Name = "Texas A&M", RivalryName = "Southwest Classic", Tier = "STATE", SeriesAge = 122 },
                new() { Team1Name = "Tennessee", Team2Name = "Florida", RivalryName = "Third Saturday in September", Tier = "STATE", SeriesAge = 109 },
                new() { Team1Name = "Florida State", Team2Name = "Clemson", RivalryName = "ACC marquee matchup since 1902", Tier = "STATE", SeriesAge = 123 },
                new() { Team1Name = "Arkansas", Team2Name = "LSU", RivalryName = "Battle for the Boot", Tier = "STATE", SeriesAge = 124 },
                new() { Team1Name = "LSU", Team2Name = "Tennessee", RivalryName = "Annual SEC rivalry since 1914", Tier = "STATE", SeriesAge = 111 },
                new() { Team1Name = "Nebraska", Team2Name = "Iowa", RivalryName = "Series goes back to 1891 (52 total meetings)", Tier = "STATE", SeriesAge = 134 },

                // MEH TIER - Expected variance ratio: 1.1x
                new() { Team1Name = "Cincinnati", Team2Name = "Miami (OH)", RivalryName = "Battle of the Bricks", Tier = "MEH", SeriesAge = 137 },
                new() { Team1Name = "Purdue", Team2Name = "Indiana", RivalryName = "Old Oaken Bucket", Tier = "MEH", SeriesAge = 134 },
                new() { Team1Name = "Kansas State", Team2Name = "Kansas", RivalryName = "Sunflower Showdown", Tier = "MEH", SeriesAge = 123 },
                new() { Team1Name = "Kentucky", Team2Name = "Tennessee", RivalryName = "133 years", Tier = "MEH", SeriesAge = 132 },
                new() { Team1Name = "NC State", Team2Name = "Wake Forest", RivalryName = "Annual ACC rivalry since 1895", Tier = "MEH", SeriesAge = 130 },
                new() { Team1Name = "TCU", Team2Name = "Baylor", RivalryName = "Revivalry", Tier = "MEH", SeriesAge = 126 },
                new() { Team1Name = "North Carolina", Team2Name = "Duke", RivalryName = "South's Oldest Rivalry", Tier = "MEH", SeriesAge = 137 },
                new() { Team1Name = "Iowa State", Team2Name = "Kansas State", RivalryName = "Annual Big 12 game", Tier = "MEH", SeriesAge = 108 },
                new() { Team1Name = "Georgia", Team2Name = "South Carolina", RivalryName = "SEC East rivalry", Tier = "MEH", SeriesAge = 131 },
                new() { Team1Name = "Houston", Team2Name = "Rice", RivalryName = "Bayou Bucket", Tier = "MEH", SeriesAge = 104 },
                new() { Team1Name = "Mississippi", Team2Name = "Arkansas", RivalryName = "Battle of the Delta", Tier = "MEH", SeriesAge = 119 },
                new() { Team1Name = "Texas A&M", Team2Name = "Texas Tech", RivalryName = "SWC/Big 12 rivals", Tier = "MEH", SeriesAge = 65 }
            };
        }

        public class RivalryMetadata
        {
            public string Team1Name { get; set; } = string.Empty;
            public string Team2Name { get; set; } = string.Empty;
            public string RivalryName { get; set; } = string.Empty;
            public string Tier { get; set; } = string.Empty;
            public int SeriesAge { get; set; }

            public double GetExpectedVarianceMultiplier()
            {
                return Tier switch
                {
                    "EPIC" => 1.75,
                    "NATIONAL" => 1.5,
                    "STATE" => 1.3,
                    "MEH" => 1.1,
                    _ => 1.0
                };
            }
        }
    }
}
