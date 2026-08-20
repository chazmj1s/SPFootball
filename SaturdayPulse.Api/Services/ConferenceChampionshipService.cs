using System;
using System.Collections.Generic;
using System.Linq;
using SaturdayPulse.Models;

namespace SaturdayPulse.Services
{
    // ─────────────────────────────────────────────────────────────────────────
    // SERVICE
    //
    // Models (ConferenceStanding, ChampionshipQualificationResult, ContenderInfo)
    // moved to SaturdayPulse.Api/Models/ — see those files for definitions.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Calculates which teams qualify for each FBS conference championship game
    /// based on the official tiebreaker rules for each conference (2025 season,
    /// plus the reconstituted 2026 Pac-12).
    ///
    /// STUB REQUIREMENTS (cannot be computed from internal data alone):
    ///   - CFP Selection Committee rankings
    ///   - AP Poll rankings
    ///   - Coaches Poll rankings
    ///   - SportSource Analytics rating score (Big 12 / Mountain West / Pac-12
    ///     final tiebreaker)
    ///   - Nationally ranked metrics composite (Mountain West multi-team tiebreaker)
    ///
    /// These are accepted as nullable inputs on ConferenceStanding and are noted
    /// in ChampionshipQualificationResult.StubsApplied when used.
    ///
    /// NOTE ON Pac-12 "next-highest common opponent": the reconstituted Pac-12
    /// plays a full 7-game round-robin among its 8 members, so every remaining
    /// conference member is, by definition, a common opponent. That tiebreaker
    /// step is implemented by walking down the standings and comparing each
    /// tied team's recorded W/L result against each opponent in turn — no
    /// separate "common opponent subset" computation is needed the way it
    /// would be for an unbalanced-schedule conference like the Big 12.
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
                "Pac-12"       => GetTopTwo_Pac12(standings),
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

        // ── Group of Six: Pac-12 (reconstituted 2026) ────────────────────────

        /// <summary>
        /// Pac-12 (2026 reconstitution): 8 members, full 7-game round-robin —
        /// every team plays every other team, so there is no "common opponent"
        /// subset to compute; every remaining conference member is automatically
        /// a common opponent. Top 2 by conference win percentage.
        ///
        /// Two-way tiebreaker: Head-to-head →
        /// record vs. next-highest common opponent (proceeding downward
        /// through the standings) → strength of conference schedule →
        /// total regular-season wins (max 1 FCS win) →
        /// [STUB: SportSource Analytics rating] → coin toss.
        ///
        /// Multi-team (3+) tiebreaker: Cumulative head-to-head record among
        /// the tied teams → record vs. common conference opponents →
        /// record vs. next-highest common opponent → strength of conference
        /// schedule → [STUB: SportSource Analytics rating] → coin toss.
        /// Per the official protocol, once the field narrows to exactly two
        /// teams at any step, the sequence reverts to the two-way tiebreaker
        /// (implemented below as a dynamic pool-size check, not a hard branch,
        /// so this happens automatically at whatever step the narrowing occurs).
        /// </summary>
        private ChampionshipQualificationResult GetTopTwo_Pac12(
            List<ConferenceStanding> standings)
        {
            var result = new ChampionshipQualificationResult
            {
                Conference = "Pac-12",
                Format     = "Top 2 by conference record (full round-robin)"
            };

            var sorted = standings.OrderByDescending(t => t.ConferenceWinPct).ToList();
            var log    = result.TiebreakerLog;

            // `sorted` is passed as the full-standings reference on every call so
            // "next-highest common opponent" always walks the true overall
            // standings, not whatever subset remains after Qualifier1 is pulled.
            var q1 = ResolveTopSpot_Pac12(sorted, sorted, log, result.StubsApplied, 1);
            var remaining = sorted.Where(t => t != q1).ToList();
            var q2 = ResolveTopSpot_Pac12(remaining, sorted, log, result.StubsApplied, 2);

            result.Qualifier1 = q1;
            result.Qualifier2 = q2;
            result.Qualifier1Method = "Conference record";
            result.Qualifier2Method = "Conference record";
            result.Contenders = GetContenders(standings, q1, q2);
            return result;
        }

        /// <summary>
        /// Pac-12-specific tiebreaker resolver. Distinct from the shared
        /// <see cref="ResolveTopSpot"/> engine because the Pac-12's official
        /// step order and step set genuinely differ between 2-way and 3+ team
        /// ties (e.g. "record vs. common opponents" only applies at 3+;
        /// "total wins" only applies at 2-way), rather than one fixed sequence
        /// used for both.
        /// </summary>
        private ConferenceStanding ResolveTopSpot_Pac12(
            List<ConferenceStanding> sorted,
            List<ConferenceStanding> fullStandings,
            List<string> log,
            List<string> stubs,
            int spot)
        {
            if (!sorted.Any()) return null;

            var best = sorted.First();
            var pool = sorted.Where(t => t.ConferenceWinPct == best.ConferenceWinPct).ToList();

            if (pool.Count == 1)
            {
                log.Add($"Spot {spot}: {best.TeamName} leads outright ({best.ConferenceWins}-{best.ConferenceLosses})");
                return pool[0];
            }

            log.Add($"Spot {spot}: {pool.Count}-way tie at {best.ConferenceWinPct:P0} — applying Pac-12 tiebreakers");

            // Step (both paths): head-to-head. For a 2-way tie this reduces to
            // the direct result of the single game between the two teams; for
            // 3+ it's cumulative round-robin record strictly among the tied
            // teams. Same underlying computation either way.
            pool = NarrowByCumulativeHeadToHead(pool, log, spot);
            if (pool.Count == 1) return pool[0];

            // Multi-team only: record against common conference opponents.
            // Skipped once the pool has already narrowed to 2 (two-way path
            // doesn't have this step).
            if (pool.Count > 2)
            {
                pool = NarrowByMetric(pool, t => t.CommonOpponentWinPct, "Common opponent WP", log, spot);
                if (pool.Count == 1) return pool[0];
            }

            // Both paths: record vs. next-highest common opponent, walking
            // down the standings. Full round-robin ⇒ every remaining team is
            // a common opponent, so no subset filtering is needed first.
            pool = NarrowByNextHighestCommonOpponent(pool, fullStandings, log, spot);
            if (pool.Count == 1) return pool[0];

            // Both paths: strength of conference schedule.
            pool = NarrowByMetric(pool, t => t.ConferenceOpponentWinPct, "Conf opponent WP / SOS", log, spot);
            if (pool.Count == 1) return pool[0];

            // Two-way only: total regular-season wins (max 1 FCS win counted —
            // assumed already reflected in OverallWins upstream). Only runs
            // once the pool is down to exactly 2, whether it started there or
            // narrowed there from a 3+ tie above.
            if (pool.Count == 2)
            {
                pool = NarrowByMetric(pool, t => (double)t.OverallWins, "Total wins (max 1 FCS)", log, spot);
                if (pool.Count == 1) return pool[0];
            }

            // Both paths: SportSource Analytics rating (final data-driven step).
            pool = NarrowBySportSource(pool, log, stubs, spot);
            if (pool.Count == 1) return pool[0];

            log.Add($"Spot {spot}: All Pac-12 tiebreakers exhausted — coin toss among {pool.Count} team(s)");
            return pool[new Random().Next(pool.Count)];
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

        // ─────────────────────────────────────────────────────────────────────
        // PAC-12 TIEBREAKER ENGINE — narrowing helpers
        //
        // These differ from the Break* helpers above in shape: instead of
        // returning a single winner-or-null, they return the narrowed pool of
        // still-tied teams (which may be 1 team = resolved, or 2+ = proceed to
        // the next step). This is what lets ResolveTopSpot_Pac12 check
        // pool.Count after each step to decide whether a 3+ tie has reverted
        // to a 2-team tie.
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Generic "narrow to the leaders by this metric" step, used for the
        /// Pac-12's common-opponent WP, SOS, and total-wins steps (all of which
        /// are plain field/metric comparisons with no special data shape).
        /// </summary>
        private List<ConferenceStanding> NarrowByMetric(
            List<ConferenceStanding> pool,
            Func<ConferenceStanding, double> metric,
            string stepLabel,
            List<string> log,
            int spot)
        {
            var best = pool.Max(metric);
            var leaders = pool.Where(t => metric(t) == best).ToList();

            log.Add(leaders.Count < pool.Count
                ? $"  Spot {spot} ({stepLabel}): narrowed to {leaders.Count} team(s) at {best:P1}"
                : $"  Spot {spot} ({stepLabel}): no separation ({best:P1})");

            return leaders;
        }

        /// <summary>
        /// Head-to-head step for the Pac-12 pipeline. For a 2-team pool this is
        /// just the direct result of the single game between them; for 3+ it's
        /// win pct in round-robin games played strictly among the current pool.
        /// Both cases fall out of the same computation since, for n=2, "games
        /// among the pool" is just the one game between those two teams.
        /// </summary>
        private List<ConferenceStanding> NarrowByCumulativeHeadToHead(
            List<ConferenceStanding> pool, List<string> log, int spot)
        {
            var poolIds = pool.Select(t => t.TeamId).ToHashSet();

            var records = pool.Select(t => new
            {
                Team = t,
                Wins = t.HeadToHeadResults.Count(kvp => poolIds.Contains(kvp.Key) && kvp.Value),
                Losses = t.HeadToHeadResults.Count(kvp => poolIds.Contains(kvp.Key) && !kvp.Value)
            }).ToList();

            double WinPct(int w, int l) => (w + l) > 0 ? (double)w / (w + l) : 0.0;

            var best = records.Max(r => WinPct(r.Wins, r.Losses));
            var leaders = records.Where(r => WinPct(r.Wins, r.Losses) == best)
                                  .Select(r => r.Team)
                                  .ToList();

            log.Add(leaders.Count < pool.Count
                ? $"  Spot {spot} (H2H among tied): narrowed to {leaders.Count} team(s) at {best:P0}"
                : $"  Spot {spot} (H2H among tied): no separation ({best:P0})");

            return leaders;
        }

        /// <summary>
        /// "Record against the next-highest common opponent, proceeding
        /// downward through the standings." Because the Pac-12 plays a full
        /// round-robin, every non-tied conference member is automatically a
        /// common opponent, so this just walks the standings from the top,
        /// comparing each tied team's recorded W/L result against that specific
        /// opponent, and narrows the pool to whichever team(s) won that game
        /// (assuming a split result — if everyone in the pool got the same
        /// result against that opponent, there's no separation and it moves to
        /// the next opponent down).
        /// </summary>
        private List<ConferenceStanding> NarrowByNextHighestCommonOpponent(
            List<ConferenceStanding> pool,
            List<ConferenceStanding> fullStandings,
            List<string> log,
            int spot)
        {
            var startingCount = pool.Count;
            var excludedIds = pool.Select(t => t.TeamId).ToHashSet();
            var opponentsInOrder = fullStandings
                .Where(t => !excludedIds.Contains(t.TeamId))
                .OrderByDescending(t => t.ConferenceWinPct)
                .ToList();

            foreach (var opponent in opponentsInOrder)
            {
                var results = pool.Select(t => new
                {
                    Team = t,
                    Result = t.HeadToHeadResults.TryGetValue(opponent.TeamId, out bool won)
                        ? (bool?)won
                        : null
                }).ToList();

                // Need a recorded result for every team in the pool to compare fairly.
                if (results.Any(r => r.Result == null)) continue;

                var winners = results.Where(r => r.Result == true).Select(r => r.Team).ToList();

                if (winners.Count > 0 && winners.Count < pool.Count)
                {
                    log.Add($"  Spot {spot} (Next-highest common opp): narrowed to {winners.Count} team(s) via result vs {opponent.TeamName}");
                    pool = winners;
                    if (pool.Count == 1) return pool;
                }
                // else: every team in the pool got the same result vs this
                // opponent (all won or all lost) — no separation, try the next
                // team down the standings.
            }

            log.Add(pool.Count < startingCount
                ? $"  Spot {spot} (Next-highest common opp): stopped at {pool.Count} team(s), standings exhausted"
                : $"  Spot {spot} (Next-highest common opp): no separation found through the standings");

            return pool;
        }

        /// <summary>
        /// SportSource Analytics rating step. Lower rating = better, matching
        /// the convention already used for Big 12 / Mountain West. Stubs out
        /// (and leaves the pool unchanged) if any tied team is missing a rating.
        /// </summary>
        private List<ConferenceStanding> NarrowBySportSource(
            List<ConferenceStanding> pool, List<string> log, List<string> stubs, int spot)
        {
            var withRating = pool.Where(t => t.SportSourceRating.HasValue).ToList();

            if (withRating.Count != pool.Count)
            {
                stubs.Add($"Spot {spot}: SportSource Analytics tiebreaker required but rating unavailable for one or more tied teams");
                log.Add($"  Spot {spot} (SportSource): STUB — rating unavailable for all tied teams");
                return pool;
            }

            var best = withRating.Min(t => t.SportSourceRating!.Value); // lower = better
            var leaders = withRating.Where(t => t.SportSourceRating == best).ToList();

            log.Add(leaders.Count < pool.Count
                ? $"  Spot {spot} (SportSource): narrowed to {leaders.Count} team(s) at rating {best}"
                : $"  Spot {spot} (SportSource): no separation at rating {best}");

            return leaders;
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
