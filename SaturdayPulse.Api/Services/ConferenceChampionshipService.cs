using System;
using System.Collections.Generic;
using System.Linq;

namespace SaturdayPulse.Services
{
    // ─────────────────────────────────────────────────────────────────────────
    // MODELS
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Represents a team's standing within its conference for championship
    /// qualification purposes.
    /// </summary>
    public class ConferenceStanding
    {
        public int    TeamId          { get; set; }
        public string TeamName        { get; set; }
        public string Conference      { get; set; }
        public string Division        { get; set; }  // null if conference has no divisions
        public int    ConferenceWins { get; set; }
        public int    ConferenceLosses { get; set; }
        public int    ActualConferenceWins { get; set; }
        public int    ActualConferenceLosses { get; set; }
        public int    OverallWins     { get; set; }
        public int    OverallLosses   { get; set; }

        // Points for/against in conference games (used for margin tiebreakers)
        public int    ConfPointsFor     { get; set; }
        public int    ConfPointsAgainst { get; set; }

        // Head-to-head results vs other teams in the conference (TeamId → W/L)
        public Dictionary<int, bool> HeadToHeadResults { get; set; } = new();

        // Win pct vs common conference opponents (populated during tiebreaker calc)
        public double CommonOpponentWinPct { get; set; }

        // Combined win pct of all conference opponents (strength of schedule proxy)
        public double ConferenceOpponentWinPct { get; set; }

        // Externally-sourced ranking — CFP, AP, Coaches Poll etc.
        // NULL means unknown / not ranked (stubs out requirements we can't compute)
        public int? CfpRanking      { get; set; }
        public int? ApRanking       { get; set; }
        public int? SportSourceRating { get; set; } // Big 12 / Mountain West tiebreaker

        public double ConferenceWinPct =>
            (ConferenceWins + ConferenceLosses) > 0
                ? (double)ConferenceWins / (ConferenceWins + ConferenceLosses)
                : 0.0;

        public double OverallWinPct =>
            (OverallWins + OverallLosses) > 0
                ? (double)OverallWins / (OverallWins + OverallLosses)
                : 0.0;
    }

    /// <summary>
    /// Result of a championship qualification calculation for one conference.
    /// </summary>
    public class ChampionshipQualificationResult
    {
        public string Conference   { get; set; }
        public string Format       { get; set; }  // "Top 2" | "Division Winners" | etc.

        // The two qualifiers (or one per division for division-based conferences)
        public ConferenceStanding Qualifier1 { get; set; }
        public ConferenceStanding Qualifier2 { get; set; }

        // How the spots were determined
        public string Qualifier1Method { get; set; }
        public string Qualifier2Method { get; set; }

        // Any tiebreaker steps that were used
        public List<string> TiebreakerLog { get; set; } = new();

        // Flags for stubs — requirements we couldn't compute from available data
        public List<string> StubsApplied { get; set; } = new();

        public List<ContenderInfo> Contenders { get; set; } = new();
    }

    public class ContenderInfo
    {
        public string TeamName { get; set; }
        public int ConferenceWins { get; set; }
        public int ConferenceLosses { get; set; }
        public int OverallWins { get; set; }
        public int OverallLosses { get; set; }
        public int ActualConferenceWins { get; set; }
        public int ActualConferenceLosses { get; set; }
        public string ConferenceRecord => $"{ConferenceWins}-{ConferenceLosses}";
        public string OverallRecord => $"{OverallWins}-{OverallLosses}";
        public string ActualConferenceRecord => $"{ActualConferenceWins}-{ActualConferenceLosses}";

    }

    // ─────────────────────────────────────────────────────────────────────────
    // SERVICE
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Calculates which teams qualify for each FBS conference championship game
    /// based on the official tiebreaker rules for each conference (2025 season).
    ///
    /// STUB REQUIREMENTS (cannot be computed from internal data alone):
    ///   - CFP Selection Committee rankings
    ///   - AP Poll rankings
    ///   - Coaches Poll rankings
    ///   - SportSource Analytics rating score (Big 12 / Mountain West final tiebreaker)
    ///   - Nationally ranked metrics composite (Mountain West multi-team tiebreaker)
    ///
    /// These are accepted as nullable inputs on ConferenceStanding and are noted
    /// in ChampionshipQualificationResult.StubsApplied when used.
    /// </summary>
    public class ConferenceChampionshipService
    {
        // ── Public entry point ───────────────────────────────────────────────

        /// <summary>
        /// Given a list of standings for all teams in a conference, returns the
        /// two qualifiers for that conference's championship game.
        /// </summary>
        public ChampionshipQualificationResult GetQualifiers(
            string conference,
            List<ConferenceStanding> standings)
        {
            return conference switch
            {
                "SEC"          => GetTopTwo_SEC(standings),
                "Big Ten"      => GetTopTwo_BigTen(standings),
                "ACC"          => GetTopTwo_ACC(standings),
                "Big 12"       => GetTopTwo_Big12(standings),
                "AAC"          => GetTopTwo_AAC(standings),
                "Mountain West"=> GetTopTwo_MountainWest(standings),
                "MAC"          => GetTopTwo_MAC(standings),
                "C-USA"        => GetTopTwo_CUSA(standings),
                "Sun Belt"     => GetDivisionWinners_SunBelt(standings),
                _              => GetTopTwo_Generic(standings, conference)
            };
        }

        // ── Power Four: SEC ──────────────────────────────────────────────────

        /// <summary>
        /// SEC: Top 2 by conference win percentage. No divisions since 2024.
        /// Tiebreaker: Head-to-head → common opponents → cumulative conf WP of
        /// conf opponents → capped scoring margin → random draw.
        /// Source: CBS Sports tiebreaker guide 2025.
        /// </summary>
        private ChampionshipQualificationResult GetTopTwo_SEC(
            List<ConferenceStanding> standings)
        {
            var result = new ChampionshipQualificationResult
            {
                Conference = "SEC",
                Format     = "Top 2 by conference record"
            };

            var sorted = standings.OrderByDescending(t => t.ConferenceWinPct).ToList();
            var log    = result.TiebreakerLog;

            var q1 = ResolveTopSpot(sorted, log, result.StubsApplied, "SEC", 1);
            var remaining = sorted.Where(t => t != q1).ToList();
            var q2 = ResolveTopSpot(remaining, log, result.StubsApplied, "SEC", 2);

            result.Qualifier1 = q1;
            result.Qualifier2 = q2;
            result.Qualifier1Method = "Conference record";
            result.Qualifier2Method = "Conference record";
            result.Contenders = GetContenders(standings, q1, q2);  // ← add this line
            return result;
        }

        // ── Power Four: Big Ten ──────────────────────────────────────────────

        /// <summary>
        /// Big Ten: Top 2 by conference win percentage. No divisions since 2024.
        /// Tiebreaker: Head-to-head → common conference opponents →
        /// next highest-placed common opponent proceeding through standings →
        /// combined WP of conf opponents → [STUB: CFP/external ranking] → coin flip.
        /// </summary>
        private ChampionshipQualificationResult GetTopTwo_BigTen(
            List<ConferenceStanding> standings)
        {
            var result = new ChampionshipQualificationResult
            {
                Conference = "Big Ten",
                Format     = "Top 2 by conference record"
            };

            var sorted = standings.OrderByDescending(t => t.ConferenceWinPct).ToList();
            var log    = result.TiebreakerLog;

            var q1 = ResolveTopSpot(sorted, log, result.StubsApplied, "Big Ten", 1);
            var remaining = sorted.Where(t => t != q1).ToList();
            var q2 = ResolveTopSpot(remaining, log, result.StubsApplied, "Big Ten", 2);

            result.Qualifier1 = q1;
            result.Qualifier2 = q2;
            result.Qualifier1Method = "Conference record";
            result.Qualifier2Method = "Conference record";
            result.Contenders = GetContenders(standings, q1, q2);  // ← add this line
            return result;
        }

        // ── Power Four: ACC ──────────────────────────────────────────────────

        /// <summary>
        /// ACC: Top 2 by conference win percentage. No divisions since 2023.
        /// Tiebreaker (2-team): Head-to-head → common opponents WP →
        /// common opponents by finish order → combined conf opponent WP →
        /// [STUB: external ranking / coin flip].
        /// Tiebreaker (3+ teams): Same sequence applied iteratively.
        /// </summary>
        private ChampionshipQualificationResult GetTopTwo_ACC(
            List<ConferenceStanding> standings)
        {
            var result = new ChampionshipQualificationResult
            {
                Conference = "ACC",
                Format     = "Top 2 by conference record"
            };

            var sorted = standings.OrderByDescending(t => t.ConferenceWinPct).ToList();
            var log    = result.TiebreakerLog;

            var q1 = ResolveTopSpot(sorted, log, result.StubsApplied, "ACC", 1);
            var remaining = sorted.Where(t => t != q1).ToList();
            var q2 = ResolveTopSpot(remaining, log, result.StubsApplied, "ACC", 2);

            result.Qualifier1 = q1;
            result.Qualifier2 = q2;
            result.Qualifier1Method = "Conference record";
            result.Qualifier2Method = "Conference record";
            result.Contenders = GetContenders(standings, q1, q2);  // ← add this line
            return result;
        }

        // ── Power Four: Big 12 ───────────────────────────────────────────────

        /// <summary>
        /// Big 12: Top 2 by conference win percentage (unbalanced schedule).
        /// Tiebreaker (2-team): Head-to-head → common conf opponent WP →
        /// next highest common opponent proceeding through standings →
        /// combined conf opponent WP → total wins (max 1 FCS win) →
        /// [STUB: SportSource Analytics rating] → coin flip.
        /// Tiebreaker (3+ teams): Head-to-head among tied group →
        /// common opponent WP → next highest common opponent →
        /// combined conf SOS → total wins → [STUB: SportSource] → coin flip.
        /// </summary>
        private ChampionshipQualificationResult GetTopTwo_Big12(
            List<ConferenceStanding> standings)
        {
            var result = new ChampionshipQualificationResult
            {
                Conference = "Big 12",
                Format     = "Top 2 by conference record (unbalanced schedule)"
            };

            var sorted = standings.OrderByDescending(t => t.ConferenceWinPct).ToList();
            var log    = result.TiebreakerLog;

            var q1 = ResolveTopSpot(sorted, log, result.StubsApplied, "Big 12", 1);
            var remaining = sorted.Where(t => t != q1).ToList();
            var q2 = ResolveTopSpot(remaining, log, result.StubsApplied, "Big 12", 2);

            result.Qualifier1 = q1;
            result.Qualifier2 = q2;
            result.Qualifier1Method = "Conference record";
            result.Qualifier2Method = "Conference record";
            result.Contenders = GetContenders(standings, q1, q2);  // ← add this line
            return result;
        }

        // ── Group of Five: AAC (American Conference) ─────────────────────────

        /// <summary>
        /// AAC: Top 2 by conference win percentage. Single table, no divisions.
        /// Tiebreaker: Head-to-head → common opponent WP → conf opponent combined WP
        /// → [STUB: external ranking] → coin flip.
        /// </summary>
        private ChampionshipQualificationResult GetTopTwo_AAC(
            List<ConferenceStanding> standings)
        {
            var result = new ChampionshipQualificationResult
            {
                Conference = "AAC",
                Format     = "Top 2 by conference record"
            };

            var sorted = standings.OrderByDescending(t => t.ConferenceWinPct).ToList();
            var log    = result.TiebreakerLog;

            var q1 = ResolveTopSpot(sorted, log, result.StubsApplied, "AAC", 1);
            var remaining = sorted.Where(t => t != q1).ToList();
            var q2 = ResolveTopSpot(remaining, log, result.StubsApplied, "AAC", 2);

            result.Qualifier1 = q1;
            result.Qualifier2 = q2;
            result.Qualifier1Method = "Conference record";
            result.Qualifier2Method = "Conference record";
            result.Contenders = GetContenders(standings, q1, q2);  // ← add this line
            return result;
        }

        // ── Group of Five: Mountain West ─────────────────────────────────────

        /// <summary>
        /// Mountain West: Top 2 by conference win percentage. No divisions since 2023.
        /// Tiebreaker: Head-to-head → common conf opponent WP →
        /// [STUB: composite average of nationally ranked metrics] →
        /// [STUB: SportSource Analytics rating] → coin flip.
        /// Note: The 2025 season saw a 4-way tie broken by the nationally ranked
        /// metrics composite — this step is stubbed.
        /// </summary>
        private ChampionshipQualificationResult GetTopTwo_MountainWest(
            List<ConferenceStanding> standings)
        {
            var result = new ChampionshipQualificationResult
            {
                Conference = "Mountain West",
                Format     = "Top 2 by conference record"
            };

            var sorted = standings.OrderByDescending(t => t.ConferenceWinPct).ToList();
            var log    = result.TiebreakerLog;

            var q1 = ResolveTopSpot(sorted, log, result.StubsApplied, "Mountain West", 1);
            var remaining = sorted.Where(t => t != q1).ToList();
            var q2 = ResolveTopSpot(remaining, log, result.StubsApplied, "Mountain West", 2);

            result.Qualifier1 = q1;
            result.Qualifier2 = q2;
            result.Qualifier1Method = "Conference record";
            result.Qualifier2Method = "Conference record";
            result.Contenders = GetContenders(standings, q1, q2);  // ← add this line
            return result;
        }

        // ── Group of Five: MAC ───────────────────────────────────────────────

        /// <summary>
        /// MAC: Top 2 by conference win percentage. Pod system (no formal divisions)
        /// since 2024. Previously East/West divisions.
        /// Tiebreaker: Head-to-head → common conf opponent WP →
        /// overall conf opponent WP → [STUB: external ranking] → coin flip.
        /// </summary>
        private ChampionshipQualificationResult GetTopTwo_MAC(
            List<ConferenceStanding> standings)
        {
            var result = new ChampionshipQualificationResult
            {
                Conference = "MAC",
                Format     = "Top 2 by conference record (pod system)"
            };

            var sorted = standings.OrderByDescending(t => t.ConferenceWinPct).ToList();
            var log    = result.TiebreakerLog;

            var q1 = ResolveTopSpot(sorted, log, result.StubsApplied, "MAC", 1);
            var remaining = sorted.Where(t => t != q1).ToList();
            var q2 = ResolveTopSpot(remaining, log, result.StubsApplied, "MAC", 2);

            result.Qualifier1 = q1;
            result.Qualifier2 = q2;
            result.Qualifier1Method = "Conference record";
            result.Qualifier2Method = "Conference record";
            result.Contenders = GetContenders(standings, q1, q2);  // ← add this line
            return result;
        }

        // ── Group of Five: Conference USA ────────────────────────────────────

        /// <summary>
        /// CUSA: Top 2 by conference win percentage. Single table, no divisions.
        /// Tiebreaker: Head-to-head → common conf opponent WP →
        /// overall conf opponent WP → [STUB: external ranking] → coin flip.
        /// </summary>
        private ChampionshipQualificationResult GetTopTwo_CUSA(
            List<ConferenceStanding> standings)
        {
            var result = new ChampionshipQualificationResult
            {
                Conference = "C-USA",
                Format     = "Top 2 by conference record"
            };

            var sorted = standings.OrderByDescending(t => t.ConferenceWinPct).ToList();
            var log    = result.TiebreakerLog;

            var q1 = ResolveTopSpot(sorted, log, result.StubsApplied, "C-USA", 1);
            var remaining = sorted.Where(t => t != q1).ToList();
            var q2 = ResolveTopSpot(remaining, log, result.StubsApplied, "C-USA", 2);

            result.Qualifier1 = q1;
            result.Qualifier2 = q2;
            result.Qualifier1Method = "Conference record";
            result.Qualifier2Method = "Conference record";
            result.Contenders = GetContenders(standings, q1, q2);  // ← add this line
            return result;
        }

        // ── Division-based: Sun Belt ─────────────────────────────────────────

        /// <summary>
        /// Sun Belt: ONLY FBS conference still using divisions as of 2025 (East/West).
        /// Each division winner qualifies. Intra-division tiebreaker:
        /// Head-to-head → common division opponent WP → all division opponent WP →
        /// head-to-head vs next highest division team → conf opponent combined WP →
        /// [STUB: external ranking] → coin flip.
        /// </summary>
        private ChampionshipQualificationResult GetDivisionWinners_SunBelt(
            List<ConferenceStanding> standings)
        {
            var result = new ChampionshipQualificationResult
            {
                Conference = "Sun Belt",
                Format     = "Division winners (East vs West)"
            };

            var east = standings.Where(t => t.Division == "East")
                                .OrderByDescending(t => t.ConferenceWinPct).ToList();
            var west = standings.Where(t => t.Division == "West")
                                .OrderByDescending(t => t.ConferenceWinPct).ToList();

            if (!east.Any() || !west.Any())
            {
                result.TiebreakerLog.Add("ERROR: Missing division data for Sun Belt");
                return result;
            }

            var q1 = ResolveTopSpot(east, result.TiebreakerLog, result.StubsApplied, "Sun Belt East", 1);
            var q2 = ResolveTopSpot(west, result.TiebreakerLog, result.StubsApplied, "Sun Belt West", 1);

            result.Qualifier1 = q1;
            result.Qualifier2 = q2;
            result.Qualifier1Method = "East Division winner";
            result.Qualifier2Method = "West Division winner";
            result.Contenders = GetContenders(standings, q1, q2);  // ← add this line
            return result;
        }

        

        // ── Generic fallback ─────────────────────────────────────────────────

        private ChampionshipQualificationResult GetTopTwo_Generic(
            List<ConferenceStanding> standings, string conference)
        {
            var result = new ChampionshipQualificationResult
            {
                Conference = conference,
                Format     = "Top 2 by conference record (generic)"
            };

            var sorted = standings.OrderByDescending(t => t.ConferenceWinPct).ToList();
            var log    = result.TiebreakerLog;

            var q1 = ResolveTopSpot(sorted, log, result.StubsApplied, conference, 1);
            var remaining = sorted.Where(t => t != q1).ToList();
            var q2 = ResolveTopSpot(remaining, log, result.StubsApplied, conference, 2);

            result.Qualifier1 = q1;
            result.Qualifier2 = q2;
            result.Qualifier1Method = "Conference record";
            result.Qualifier2Method = "Conference record";
            result.Contenders = GetContenders(standings, q1, q2);  // ← add this line
            return result;
        }

        // ─────────────────────────────────────────────────────────────────────
        // TIEBREAKER ENGINE
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Resolves the top team from a sorted list, applying tiebreaker logic
        /// when multiple teams share the same conference win percentage.
        /// </summary>
        private ConferenceStanding ResolveTopSpot(
            List<ConferenceStanding> sorted,
            List<string> log,
            List<string> stubs,
            string confLabel,
            int spot)
        {
            if (!sorted.Any()) return null;

            var best = sorted.First();
            var tied = sorted.Where(t => t.ConferenceWinPct == best.ConferenceWinPct).ToList();

            if (tied.Count == 1)
            {
                log.Add($"Spot {spot}: {best.TeamName} leads outright ({best.ConferenceWins}-{best.ConferenceLosses})");
                return best;
            }

            log.Add($"Spot {spot}: {tied.Count}-way tie at {best.ConferenceWinPct:P0} — applying tiebreakers");

            // ── Step 1: Head-to-head among tied teams ─────────────────────────
            var afterH2H = BreakByHeadToHead(tied, log, spot);
            if (afterH2H != null) return afterH2H;

            // ── Step 2: Win pct vs common conference opponents ────────────────
            var afterCommon = BreakByCommonOpponents(tied, log, spot);
            if (afterCommon != null) return afterCommon;

            // ── Step 3: Combined conf opponent win pct (SOS proxy) ────────────
            var afterSOS = BreakByConferenceOpponentWinPct(tied, log, spot);
            if (afterSOS != null) return afterSOS;

            // ── Step 4: Overall wins (Big 12 specific — max 1 FCS win) ────────
            var afterWins = BreakByOverallWins(tied, log, spot);
            if (afterWins != null) return afterWins;

            // ── Step 5: External ranking (STUB) ──────────────────────────────
            var afterRanking = BreakByExternalRanking(tied, log, stubs, spot);
            if (afterRanking != null) return afterRanking;

            // ── Step 6: Coin flip (random) ────────────────────────────────────
            log.Add($"Spot {spot}: All tiebreakers exhausted — random selection");
            return tied[new Random().Next(tied.Count)];
        }

        private ConferenceStanding BreakByHeadToHead(
     List<ConferenceStanding> tied, List<string> log, int spot)
        {
            var tiedIds = tied.Select(t => t.TeamId).ToHashSet();

            if (tied.Count == 2)
            {
                var a = tied[0]; var b = tied[1];
                if (a.HeadToHeadResults.TryGetValue(b.TeamId, out bool aWon))
                {
                    var winner = aWon ? a : b;
                    log.Add($"  Spot {spot} TB1 (H2H): {winner.TeamName} wins head-to-head");
                    return winner;
                }
                log.Add($"  Spot {spot} TB1 (H2H): No head-to-head matchup between tied teams");
                return null;
            }

            // Multi-team: find H2H record among tied teams only
            var h2hRecords = tied.Select(t => new
            {
                Team = t,
                Wins = t.HeadToHeadResults
                         .Where(kvp => tiedIds.Contains(kvp.Key) && kvp.Value)
                         .Count(),
                Losses = t.HeadToHeadResults
                           .Where(kvp => tiedIds.Contains(kvp.Key) && !kvp.Value)
                           .Count()
            }).ToList();

            // One team beat ALL others in the tied group
            var beatAll = h2hRecords.FirstOrDefault(r => r.Wins == tied.Count - 1);
            if (beatAll != null)
            {
                log.Add($"  Spot {spot} TB1 (H2H): {beatAll.Team.TeamName} beat all other tied teams");
                return beatAll.Team;
            }

            // Best H2H win pct among tied games
            var best = h2hRecords.Max(r => r.Wins + r.Losses > 0
                ? (double)r.Wins / (r.Wins + r.Losses) : 0.0);
            var leaders = h2hRecords
                .Where(r => r.Wins + r.Losses > 0 &&
                            (double)r.Wins / (r.Wins + r.Losses) == best)
                .ToList();

            if (leaders.Count == 1)
            {
                log.Add($"  Spot {spot} TB1 (H2H): {leaders[0].Team.TeamName} best H2H record among tied teams");
                return leaders[0].Team;
            }

            log.Add($"  Spot {spot} TB1 (H2H): No clear H2H winner — proceeding");
            return null;
        }

        private ConferenceStanding BreakByCommonOpponents(
            List<ConferenceStanding> tied, List<string> log, int spot)
        {
            // Use pre-computed CommonOpponentWinPct
            var best = tied.Max(t => t.CommonOpponentWinPct);
            var leaders = tied.Where(t => t.CommonOpponentWinPct == best).ToList();

            if (leaders.Count == 1)
            {
                log.Add($"  Spot {spot} TB2 (Common opp WP): {leaders[0].TeamName} ({best:P1})");
                return leaders[0];
            }

            log.Add($"  Spot {spot} TB2 (Common opp WP): Still tied at {best:P1}");
            return null;
        }

        private ConferenceStanding BreakByConferenceOpponentWinPct(
            List<ConferenceStanding> tied, List<string> log, int spot)
        {
            var best = tied.Max(t => t.ConferenceOpponentWinPct);
            var leaders = tied.Where(t => t.ConferenceOpponentWinPct == best).ToList();

            if (leaders.Count == 1)
            {
                log.Add($"  Spot {spot} TB3 (Conf opp WP/SOS): {leaders[0].TeamName} ({best:P1})");
                return leaders[0];
            }

            log.Add($"  Spot {spot} TB3 (Conf opp WP/SOS): Still tied at {best:P1}");
            return null;
        }

        private ConferenceStanding BreakByOverallWins(
            List<ConferenceStanding> tied, List<string> log, int spot)
        {
            var best = tied.Max(t => t.OverallWins);
            var leaders = tied.Where(t => t.OverallWins == best).ToList();

            if (leaders.Count == 1)
            {
                log.Add($"  Spot {spot} TB4 (Overall wins): {leaders[0].TeamName} ({best} wins)");
                return leaders[0];
            }

            log.Add($"  Spot {spot} TB4 (Overall wins): Still tied at {best}");
            return null;
        }

        private ConferenceStanding BreakByExternalRanking(
            List<ConferenceStanding> tied, List<string> log, List<string> stubs, int spot)
        {
            // Try SportSource first (Big 12 / Mountain West), then CFP, then AP
            var withSS  = tied.Where(t => t.SportSourceRating.HasValue).ToList();
            var withCFP = tied.Where(t => t.CfpRanking.HasValue).ToList();
            var withAP  = tied.Where(t => t.ApRanking.HasValue).ToList();

            if (withSS.Count == tied.Count)
            {
                var best = withSS.Min(t => t.SportSourceRating!.Value); // lower = better
                var leader = withSS.FirstOrDefault(t => t.SportSourceRating == best);
                if (leader != null)
                {
                    log.Add($"  Spot {spot} TB5 (SportSource): {leader.TeamName} (rating {best})");
                    return leader;
                }
            }

            if (withCFP.Count == tied.Count)
            {
                var best = withCFP.Min(t => t.CfpRanking!.Value);
                var leader = withCFP.FirstOrDefault(t => t.CfpRanking == best);
                if (leader != null)
                {
                    log.Add($"  Spot {spot} TB5 (CFP ranking): {leader.TeamName} (#{best})");
                    return leader;
                }
            }

            if (withAP.Count == tied.Count)
            {
                var best = withAP.Min(t => t.ApRanking!.Value);
                var leader = withAP.FirstOrDefault(t => t.ApRanking == best);
                if (leader != null)
                {
                    log.Add($"  Spot {spot} TB5 (AP ranking): {leader.TeamName} (#{best})");
                    return leader;
                }
            }

            stubs.Add($"Spot {spot}: External ranking tiebreaker required but rankings not available — random used");
            log.Add($"  Spot {spot} TB5 (External ranking): STUB — rankings unavailable");
            return null;
        }

        private static List<ContenderInfo> GetContenders(
            List<ConferenceStanding> allStandings,
            ConferenceStanding qualifier1,
            ConferenceStanding qualifier2)
            {
                if (allStandings.Count == 0) return new();

                // A team is a contender if:
                // - Not already a qualifier
                // - Within 1 game of the #2 qualifier's win pct
                // - Has at least 1 conference game played
                var q2WinPct = qualifier2?.ConferenceWinPct ?? 0.0;
                var cutoff = q2WinPct - (1.0 / Math.Max(1,
                    (qualifier2?.ConferenceWins ?? 0) + (qualifier2?.ConferenceLosses ?? 0)));

                return allStandings
                    .Where(t => t != qualifier1 &&
                                t != qualifier2 &&
                                t.ConferenceWinPct >= cutoff &&
                                (t.ConferenceWins + t.ConferenceLosses) > 0)
                    .OrderByDescending(t => t.ConferenceWinPct)
                    .ThenByDescending(t => t.ConferenceWins)
                    .Select(t => new ContenderInfo
                    {
                        TeamName = t.TeamName,
                        ConferenceWins = t.ConferenceWins,
                        ConferenceLosses = t.ConferenceLosses,
                        ActualConferenceWins = t.ActualConferenceWins,
                        ActualConferenceLosses = t.ActualConferenceLosses
                    })
                    .ToList();
            }
    }
}
