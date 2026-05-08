# NCAA Power Ratings — Team Metrics Documentation

## Overview

The system computes six core metrics per team per season, building from raw game results
up through a final composite ranking. Each metric feeds the next in a deliberate pipeline.
The same pipeline runs at season end (via `TeamMetricsService`) and at every weekly
snapshot (via `WeeklyRankingsService`).

---

## 1. Projected Wins

**What it is:** A pre-season baseline win expectation for each team, derived purely from
historical performance before the current season begins.

**How it's calculated:**
- Pulls the last 10 seasons of team records (excluding the current year)
- Normalizes each season's win percentage to a 12-game standard. Seasons with fewer than
  12 games use the actual win percentage. Seasons with more than 12 games apply a 0.25
  bump per extra win beyond 12 — rewarding teams for postseason success without
  overweighting it
- Applies a linearly increasing weight to each year, oldest = weight 1, most recent =
  weight N, so recent performance matters more than distant history
- Multiplies the weighted average by 12 to produce projected wins, rounded up only if the
  fractional part is >= 0.75

**What it's used for:**
- Seeds the SOS calculation at week 0 (season initialization) before any games are played
- Seeds the early-season Z-score calculation (weeks 1-5) before current year data is
  meaningful
- Drives the Projections tab's win projections for unplayed games

---

## 2. Strength of Schedule (SOS)

Three related values are computed: `BaseSOS`, `SubSOS`, and `CombinedSOS`.

### BaseSOS (First-Order SOS)

**What it is:** A measure of how difficult a team's actual opponents have been, weighted
by how the team performed against each opponent.

**How it's calculated:**
- For each game a team played, looks up the opponent's win percentage (bucketed to 5%
  increments)
- Looks up the expected score margin for that win percentage matchup from `AvgScoreDeltas`
- Adjusts for home field advantage and rivalry variance
- Assigns a performance weight to each game based on how the team's Z-score fell:
  - Z >= 1.0 → weight 1.25 (dominant performance)
  - Z > -1.0 → weight 1.00 (expected performance)
  - Z > -2.0 → weight 0.75 (below expectation)
  - Z <= -2.0 → weight 0.50 (significantly underperformed)
- FCS opponents are discounted to 25% weight
- BaseSOS = sum(performanceWeight × divisionWeight) / sum(divisionWeight)

**What it means:** A team that beat strong opponents convincingly gets a higher BaseSOS
than a team that squeaked by weaker ones.

### SubSOS (Second-Order SOS)

**What it is:** The average BaseSOS of a team's opponents — essentially "how hard were
your opponents' schedules?"

**How it's calculated:**
- For each opponent a team faced, retrieves that opponent's BaseSOS
- Weights each opponent's BaseSOS by the team's performance weight for that game
- SubSOS = sum(opponentBaseSOS × performanceWeight) / sum(performanceWeight)

**What it means:** Captures schedule quality two levels deep. A team that played opponents
who also played hard schedules gets rewarded even if those opponents had mediocre records.

### CombinedSOS

**What it is:** The final blended strength of schedule value used in all downstream
calculations.

**Formula:** `CombinedSOS = (2 × BaseSOS + 3 × SubSOS) / 5`

This is a 40/60 split — 40% direct opponent quality, 60% opponents-of-opponents quality.
The heavier SubSOS weighting emphasizes sustained schedule difficulty over a single
marquee game.

**Week threshold:** SOS is not recalculated during weeks 1-5. It returns early (no-op)
because win percentages are too noisy with only a handful of games. Week 6 onwards uses
current year data.

---

## 3. AvgScoreDeltas (Historical Expectation Table)

**What it is:** A lookup table that answers the question: "When a team with win percentage
X plays a team with win percentage Y, what is the expected score margin and how much does
it typically vary?"

**How it's calculated (via `ScoreDeltaCalculator`):**
- Pulls all historical games
- For each game, buckets both teams' season win percentages to the nearest 5% increment
- Normalizes so the higher-win-percentage team is always "Team 1"
- Groups all games by (Team1WinPct, Team2WinPct) bucket pair
- For each bucket computes:
  - `AverageScoreDelta` — mean margin from Team 1's perspective
  - `StDevP` — population standard deviation of the margin
  - `SampleSize` — number of games in the bucket

**What it's used for:** Every Z-score calculation in the system. It's the denominator
that turns raw point margins into standardized, context-aware performance measures.
A 10-point win means something very different when you were a 10-point favorite vs.
a 10-point underdog.

---

## 4. Power Rating

**What it is:** A measure of how a team has performed relative to expectations across
all their games, adjusted for schedule quality.

**Formula:** `PowerRating = AverageZScore × CombinedSOS`

### Per-Game Z-Score

For each game played, the system calculates:

1. **Expected margin** — looks up (Team1WinPct, Team2WinPct) in `AvgScoreDeltas` to get
   `AverageScoreDelta`, then orients it from the team's perspective (positive if favored,
   negative if underdog)

2. **Home field adjustment** — adds `HomeFieldAdvantage` points if the team is home,
   subtracts if away, no adjustment for neutral site

3. **Rivalry variance adjustment** — multiplies `StDevP` by a tier-based factor to
   widen the expected range for historically unpredictable matchups:
   - EPIC rivalries: 1.75×
   - NATIONAL rivalries: 1.50×
   - STATE rivalries: 1.30×
   - MEH rivalries: 1.10×

4. **Raw Z-score** — `(actualMargin - expectedMargin) / effectiveStDev`

5. **Logarithmic dampening** — `sign × log(1 + |zScore|)` compresses extreme values.
   A 40-point blowout beyond expectation is impressive but not twice as impressive as
   a 20-point blowout. This prevents one outlier game from dominating the season rating.

6. **Division weight** — FCS opponents contribute 0.25 weight, FBS opponents 1.0.
   A dominant win over an FCS school moves the needle 25% as much as the same win over
   an FBS school.

### Season AverageZScore

`AverageZScore = sum(zScore × divisionWeight) / sum(divisionWeight)`

A weighted average across all games. Games against FBS opponents count four times as
much as games against FCS opponents.

### PowerRating

`PowerRating = AverageZScore × CombinedSOS`

Multiplying by CombinedSOS means a team that consistently beat expectations against a
tough schedule gets a much higher PowerRating than a team that beat expectations against
a weak one.

- **Positive** — team consistently outperformed expectations
- **Near zero** — team performed roughly as expected
- **Negative** — team consistently underperformed expectations

---

## 5. Ranking

**What it is:** The composite score used for overall team ordering. Balances win-loss
record, schedule difficulty, and performance quality into a single sortable number.

**Formula:** `Ranking = WinPct × CombinedSOS × (1 + PowerRating)`

- `WinPct` — wins / (wins + losses). Teams that win are rewarded directly.
- `CombinedSOS` — scales the ranking up for teams that won against hard schedules,
  down for teams that padded their record against weak competition.
- `(1 + PowerRating)` — a multiplier that amplifies rankings for teams that dominated
  their opponents and deflates rankings for teams that barely survived.

A team with a great record, tough schedule, and dominant performances will have all
three components pushing the ranking higher simultaneously.

---

## 6. Offensive Index and Defensive Index (New — Weekly Only)

**What they are:** Separate Z-scores for each side of the ball, measuring how well a
team scored and defended relative to what was expected in each specific game context.

**How they're calculated:**

The expected margin from `AvgScoreDeltas` is decomposed into two per-side expectations:

```
ExpectedTeamScore     = leagueAvgScore + (expectedMarginFromTeamPerspective / 2)
ExpectedOpponentScore = leagueAvgScore - (expectedMarginFromTeamPerspective / 2)
```

**Offensive Z-Score per game:**
`(ActualTeamScore - ExpectedTeamScore) / effectiveStDev`
Positive = scored more than the matchup context predicted.

**Defensive Z-Score per game:**
`(ExpectedOpponentScore - ActualOpponentScore) / effectiveStDev`
Positive = held the opponent below what the matchup context predicted.

Both use the same logarithmic dampening and rivalry variance adjustments as the composite
Z-score, keeping all three metrics on the same scale.

Season averages use the same division-weighted averaging as PowerRating — FCS games
count 25%.

**OffensiveRank / DefensiveRank** — ordinal rank among all FBS teams with at least one
game played. Rank 1 = best in FBS. Rank 0 = no games played.

**What they mean in plain English:** "Given who you played and where, did your offense
score more or less than expected? Did your defense hold opponents above or below what
was expected?" A team can have a great overall record but negative indexes — meaning
they won, but not by as much as the matchup suggested they should have. That's the
"weren't they supposed to do better than this?" signal.

---

## Pipeline Order

```
Raw game results
       ↓
AvgScoreDeltas (historical expectation lookup)
       ↓
Per-game Z-scores (composite + offensive + defensive)
       ↓
BaseSOS (first-order schedule quality)
       ↓
SubSOS (second-order schedule quality)
       ↓
CombinedSOS (40% Base + 60% Sub)
       ↓
PowerRating (AvgZScore × CombinedSOS)
       ↓
Ranking (WinPct × CombinedSOS × (1 + PowerRating))
       ↓
OverallRank + TierRank + OffensiveRank + DefensiveRank
```

---

## Where Each Metric Lives

| Metric | TeamRecords | WeeklyRankings |
|---|---|---|
| Projected Wins | seed only | seed only |
| BaseSOS | ✓ | ✓ |
| SubSOS | ✓ | ✓ |
| CombinedSOS | ✓ | ✓ |
| PowerRating | ✓ | ✓ |
| Ranking | ✓ | ✓ |
| OverallRank | computed at query time | ✓ stored |
| TierRank | computed at query time | ✓ stored |
| AvgPointsScored | — | ✓ |
| AvgPointsAllowed | — | ✓ |
| OffensiveZScore | — | ✓ |
| DefensiveZScore | — | ✓ |
| OffensiveRank | — | ✓ |
| DefensiveRank | — | ✓ |

`TeamRecords` holds the final season values. `WeeklyRankings` holds point-in-time
snapshots at every week boundary, enabling historical browsing and trend analysis.
