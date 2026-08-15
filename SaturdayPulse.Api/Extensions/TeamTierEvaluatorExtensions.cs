namespace SaturdayPulse.Api.Extensions
{
    using SaturdayPulse.Api.Configuration;
    using SaturdayPulse.Models;

    public static class TeamTierEvaluatorExtensions
    {
        /// <summary>
        /// Tier classification for a team in a specific season, using that team's
        /// ACTUAL conference for seasonYear.
        ///
        /// conferenceAbbreviation must be resolved by the caller via
        /// TeamsConferenceHistory (year-aware) — NOT read from Teams.Conference,
        /// which is a single static current-conference FK and cannot reflect a
        /// team's historical conference membership. Passing a team's CURRENT
        /// conference for a historical seasonYear silently misclassifies every
        /// realignment-affected team-season — this was the original bug in this
        /// method before the fix.
        ///
        /// Matching is StringComparison.OrdinalIgnoreCase, so "Ind" vs "IND" (or
        /// any other casing drift in the Conferences table) does not affect the
        /// result here.
        /// </summary>
        public static bool IsTierOne(this Teams team, int seasonYear, string? conferenceAbbreviation)
        {
            // 1. Guard check: team must exist and a conference must have been
            //    resolved for this seasonYear by the caller.
            if (team == null || string.IsNullOrEmpty(conferenceAbbreviation))
            {
                return false;
            }

            // 2. Scan unified rules list for a valid chronological match
            var matchingRule = PowerRatingConfiguration.TierRules
                .FirstOrDefault(rule =>
                    // Condition A: Match specific team within an independent tracking window
                    ((rule.TeamId == team.TeamId && rule.ConferenceAbbreviation!.Equals(conferenceAbbreviation, StringComparison.OrdinalIgnoreCase)) ||
                     // Condition B: Match globally across the entire conference structure
                     (rule.TeamId == null && rule.ConferenceAbbreviation!.Equals(conferenceAbbreviation, StringComparison.OrdinalIgnoreCase)))
                    && seasonYear >= rule.StartYear
                    && seasonYear <= rule.EndYear);

            // 3. Return the rule's target tier value; fallback to Tier 2 (false) if unmatched
            return matchingRule?.IsTier1 ?? false;
        }
    }
}
