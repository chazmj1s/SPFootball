// Rivalry seed data based on historical analysis
// Source: D1 FBS Top 50 Rivalries spreadsheet, plus two additions (Kansas-Missouri,
// Nebraska-Oklahoma) for realignment-dormant rivalries that still belong on the list —
// same precedent as Bedlam (Oklahoma-Oklahoma State), which is also currently inactive
// post-realignment but remains seeded.
//
// Tier meanings:
//   EPIC: Century+ history, anything can happen, highest variance
//   NATIONAL: Major cross-regional rivalries, significant variance
//   STATE: In-state or regional rivalries, moderate to high variance
//   MEH: Lower-tier rivalries with regional significance but limited national appeal
//
// FIXED: "Mississippi" → "Ole Miss" on two entries (Egg Bowl, Battle of the Delta).
// This file used "Ole Miss" successfully elsewhere (the LSU-Ole Miss entry, which
// resolves and is present in the live MatchupHistory table) but "Mississippi" on these
// two, which MatchupHistoryCalculator's exact TeamName/Alias match couldn't resolve —
// the actual cause of the seed file's 50 entries only ever producing 48 live rows.
//
// CONVERTED: SeriesAge (int, "years old" relative to whenever this file was last
// edited) → FirstPlayed (int, absolute year). SeriesAge silently went stale every
// year that passed without a re-edit — a value written as accurate in one year
// would quietly drift wrong the next, with nothing to catch it. FirstPlayed
// doesn't decay. Converted via FirstPlayed = 2025 - SeriesAge, using Texas-Oklahoma
// (Red River Shootout, SeriesAge 125 → FirstPlayed 1900) as the verification check.
//
// NOTE: MatchupHistoryCalculator.CalculateAllMatchupHistories never actually read
// SeriesAge — the live MatchupHistory.FirstPlayed column is computed independently
// from real game data (games.Min(g => g.Year)), which is already accurate and
// unaffected by this file either way. This conversion is a seed-data-hygiene fix,
// not a live-data bug fix — nothing downstream was consuming the stale value.

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
                new() { Team1Name = "Ohio State", Team2Name = "Michigan", RivalryName = "The Game", Tier = "EPIC", FirstPlayed = 1897 },
                new() { Team1Name = "Alabama", Team2Name = "Auburn", RivalryName = "Iron Bowl", Tier = "EPIC", FirstPlayed = 1893 },
                new() { Team1Name = "Texas", Team2Name = "Oklahoma", RivalryName = "Red River Shootout", Tier = "EPIC", FirstPlayed = 1900 },

                // NATIONAL TIER - Expected variance ratio: 1.5x
                new() { Team1Name = "Army", Team2Name = "Navy", RivalryName = "125 meetings, never permanently cancelled", Tier = "NATIONAL", FirstPlayed = 1890 },
                new() { Team1Name = "LSU", Team2Name = "Alabama", RivalryName = "Third Saturday in November", Tier = "NATIONAL", FirstPlayed = 1895 },
                new() { Team1Name = "Tennessee", Team2Name = "Alabama", RivalryName = "Third Saturday in October", Tier = "NATIONAL", FirstPlayed = 1901 },
                new() { Team1Name = "Florida", Team2Name = "Georgia", RivalryName = "World's Largest Outdoor Cocktail Party", Tier = "NATIONAL", FirstPlayed = 1904 },
                new() { Team1Name = "Notre Dame", Team2Name = "USC", RivalryName = "Classic intersectional since 1926", Tier = "NATIONAL", FirstPlayed = 1926 },
                new() { Team1Name = "Penn State", Team2Name = "Ohio State", RivalryName = "Whiteout vs Horseshoe", Tier = "NATIONAL", FirstPlayed = 1912 },
                new() { Team1Name = "Miami", Team2Name = "Florida State", RivalryName = "Wide Right I, II, III", Tier = "NATIONAL", FirstPlayed = 1951 },
                new() { Team1Name = "Florida", Team2Name = "Florida State", RivalryName = "Sunshine Showdown", Tier = "NATIONAL", FirstPlayed = 1958 },

                // STATE TIER - Expected variance ratio: 1.3x
                new() { Team1Name = "Wisconsin", Team2Name = "Minnesota", RivalryName = "Most-played FBS series ever", Tier = "STATE", FirstPlayed = 1890 },
                new() { Team1Name = "Minnesota", Team2Name = "Iowa", RivalryName = "Floyd of Rosedale (bronze pig)", Tier = "STATE", FirstPlayed = 1891 },
                new() { Team1Name = "Auburn", Team2Name = "Georgia", RivalryName = "Deep South's Oldest Rivalry", Tier = "STATE", FirstPlayed = 1892 },
                new() { Team1Name = "Stanford", Team2Name = "California", RivalryName = "The Big Game", Tier = "STATE", FirstPlayed = 1892 },
                new() { Team1Name = "Ole Miss", Team2Name = "Mississippi State", RivalryName = "Egg Bowl", Tier = "STATE", FirstPlayed = 1901 },
                new() { Team1Name = "Oregon", Team2Name = "Oregon State", RivalryName = "Civil War (renamed)", Tier = "STATE", FirstPlayed = 1894 },
                new() { Team1Name = "Clemson", Team2Name = "South Carolina", RivalryName = "Palmetto Bowl", Tier = "STATE", FirstPlayed = 1896 },
                new() { Team1Name = "Texas", Team2Name = "Texas A&M", RivalryName = "118 meetings", Tier = "STATE", FirstPlayed = 1894 },
                new() { Team1Name = "Oklahoma", Team2Name = "Oklahoma State", RivalryName = "Bedlam", Tier = "STATE", FirstPlayed = 1904 },
                new() { Team1Name = "Washington", Team2Name = "Washington State", RivalryName = "Apple Cup", Tier = "STATE", FirstPlayed = 1900 },
                new() { Team1Name = "Michigan", Team2Name = "Michigan State", RivalryName = "Paul Bunyan Trophy", Tier = "STATE", FirstPlayed = 1898 },
                new() { Team1Name = "Georgia", Team2Name = "Georgia Tech", RivalryName = "Clean, Old-Fashioned Hate", Tier = "STATE", FirstPlayed = 1893 },
                new() { Team1Name = "North Carolina", Team2Name = "NC State", RivalryName = "Tobacco Road rivalry", Tier = "STATE", FirstPlayed = 1894 },
                new() { Team1Name = "Utah", Team2Name = "BYU", RivalryName = "Holy War", Tier = "STATE", FirstPlayed = 1896 },
                new() { Team1Name = "Arizona", Team2Name = "Arizona State", RivalryName = "Territorial Cup", Tier = "STATE", FirstPlayed = 1899 },
                new() { Team1Name = "Virginia", Team2Name = "Virginia Tech", RivalryName = "Commonwealth Clash", Tier = "STATE", FirstPlayed = 1895 },
                new() { Team1Name = "Pittsburgh", Team2Name = "Penn State", RivalryName = "Keystone State rivalry", Tier = "STATE", FirstPlayed = 1893 },
                new() { Team1Name = "UCLA", Team2Name = "USC", RivalryName = "Crosstown Showdown", Tier = "STATE", FirstPlayed = 1929 },
                new() { Team1Name = "Colorado", Team2Name = "Colorado State", RivalryName = "Rocky Mountain Showdown", Tier = "STATE", FirstPlayed = 1893 },
                new() { Team1Name = "LSU", Team2Name = "Ole Miss", RivalryName = "Annual SEC West showdown", Tier = "STATE", FirstPlayed = 1894 },
                new() { Team1Name = "Iowa", Team2Name = "Iowa State", RivalryName = "Cy-Hawk Trophy", Tier = "STATE", FirstPlayed = 1894 },
                new() { Team1Name = "Arkansas", Team2Name = "Texas A&M", RivalryName = "Southwest Classic", Tier = "STATE", FirstPlayed = 1903 },
                new() { Team1Name = "Tennessee", Team2Name = "Florida", RivalryName = "Third Saturday in September", Tier = "STATE", FirstPlayed = 1916 },
                new() { Team1Name = "Florida State", Team2Name = "Clemson", RivalryName = "ACC marquee matchup since 1902", Tier = "STATE", FirstPlayed = 1902 },
                new() { Team1Name = "Arkansas", Team2Name = "LSU", RivalryName = "Battle for the Boot", Tier = "STATE", FirstPlayed = 1901 },
                new() { Team1Name = "LSU", Team2Name = "Tennessee", RivalryName = "Annual SEC rivalry since 1914", Tier = "STATE", FirstPlayed = 1914 },
                new() { Team1Name = "Nebraska", Team2Name = "Iowa", RivalryName = "Series goes back to 1891 (52 total meetings)", Tier = "STATE", FirstPlayed = 1891 },

                // Added — realignment-dormant but belongs on the list, same precedent
                // as Bedlam above (Oklahoma-Oklahoma State, also currently inactive
                // post-realignment). FirstPlayed for these two was estimated from a
                // rough series age, not sourced directly — worth double-checking
                // against your own source rather than treating as authoritative.
                new() { Team1Name = "Kansas", Team2Name = "Missouri", RivalryName = "Border War", Tier = "STATE", FirstPlayed = 1891 },
                new() { Team1Name = "Nebraska", Team2Name = "Oklahoma", RivalryName = "Longtime Big Eight/Big 12 rivals, dormant since Nebraska's move to the Big Ten", Tier = "STATE", FirstPlayed = 1912 },

                // MEH TIER - Expected variance ratio: 1.1x
                new() { Team1Name = "Cincinnati", Team2Name = "Miami (OH)", RivalryName = "Battle of the Bricks", Tier = "MEH", FirstPlayed = 1888 },
                new() { Team1Name = "Purdue", Team2Name = "Indiana", RivalryName = "Old Oaken Bucket", Tier = "MEH", FirstPlayed = 1891 },
                new() { Team1Name = "Kansas State", Team2Name = "Kansas", RivalryName = "Sunflower Showdown", Tier = "MEH", FirstPlayed = 1902 },
                new() { Team1Name = "Kentucky", Team2Name = "Tennessee", RivalryName = "133 years", Tier = "MEH", FirstPlayed = 1893 },
                new() { Team1Name = "NC State", Team2Name = "Wake Forest", RivalryName = "Annual ACC rivalry since 1895", Tier = "MEH", FirstPlayed = 1895 },
                new() { Team1Name = "TCU", Team2Name = "Baylor", RivalryName = "Revivalry", Tier = "MEH", FirstPlayed = 1899 },
                new() { Team1Name = "North Carolina", Team2Name = "Duke", RivalryName = "South's Oldest Rivalry", Tier = "MEH", FirstPlayed = 1888 },
                new() { Team1Name = "Iowa State", Team2Name = "Kansas State", RivalryName = "Annual Big 12 game", Tier = "MEH", FirstPlayed = 1917 },
                new() { Team1Name = "Georgia", Team2Name = "South Carolina", RivalryName = "SEC East rivalry", Tier = "MEH", FirstPlayed = 1894 },
                new() { Team1Name = "Houston", Team2Name = "Rice", RivalryName = "Bayou Bucket", Tier = "MEH", FirstPlayed = 1921 },
                new() { Team1Name = "Ole Miss", Team2Name = "Arkansas", RivalryName = "Battle of the Delta", Tier = "MEH", FirstPlayed = 1906 },
                new() { Team1Name = "Texas A&M", Team2Name = "Texas Tech", RivalryName = "SWC/Big 12 rivals", Tier = "MEH", FirstPlayed = 1960 }
            };
        }

        public class RivalryMetadata
        {
            public string Team1Name { get; set; } = string.Empty;
            public string Team2Name { get; set; } = string.Empty;
            public string RivalryName { get; set; } = string.Empty;
            public string Tier { get; set; } = string.Empty;
            public int FirstPlayed { get; set; }

            /// <summary>
            /// NOTE: not called anywhere I've seen in this codebase — MatchupHistoryCalculator
            /// only reads Tier as a string and stores it on MatchupHistory; it doesn't call
            /// this method. Left as-is (not removed) since nothing in this pass depends on it
            /// either way, but flagging it as a second possible sighting of the same
            /// hand-picked-tier-constants pattern already addressed for the prediction-display
            /// path (RatingCalculator.RivalryVarianceMultiplierForDisplay/
            /// RivalryScoringAdjustment) — worth checking for real callers before trusting or
            /// removing it.
            /// </summary>
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
