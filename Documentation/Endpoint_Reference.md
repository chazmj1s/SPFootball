# NCAA Power Ratings — Endpoint Reference

## DeveloperController
**Route prefix:** `/api/developer`
**Purpose:** Administration, data loading, metric calculation, diagnostics.
**Intended client:** Web admin console (not the mobile app).
**Auth requirement:** Admin role (to be implemented).

---

### Data Loading

#### `GET initialGamesExtract`
Scrapes game data from the web starting from `startYear` through the current year.
- **Params:** `startYear` (optional, defaults to current year)
- **Writes:** `Game` table
- **Notes:** The original web data source blacklisted the server after a full 60-year
  scrape in one run. Use cautiously against external sources. `loadGameHistoryFromFiles`
  is the preferred bulk load method.

#### `GET loadGameHistoryFromFiles`
Loads all game history from local `.txt` files in the `NCAA Raw Game Data` directory.
One file per year (e.g. `2024.txt`). Preferred method for bulk historical loading.
- **Params:** None
- **Writes:** `Game` table
- **Notes:** Uses `ColumnMap` to handle pre-2013 vs post-2013 column layout differences.

#### `POST processSingleFile`
Processes a single named file from the `NCAA Raw Game Data` directory.
- **Params:** `filePath` (required, full path)
- **Writes:** `Game` table

#### `POST updateWeekGamesFromFile`
Loads games for a specific year/week from a local file, then runs the full metrics
pipeline. Use when you have a downloaded data file for the week.
- **Params:** `year` (required), `week` (required)
- **Writes:** `Game`, `TeamRecords` (via `UpdateTeamRecordsAsync`)
- **Then runs:** `SetSOS → CalculatePowerRatings → CalculateRankings`
- **Notes:** Intentionally kept separate from `updateWeekGames` until a reliable
  live data source is confirmed.

#### `POST updateWeekGames`
Fetches games for a specific year/week from a live web source, then runs partial metrics.
Use when a reliable live data source is available.
- **Params:** `year` (required), `week` (required)
- **Writes:** `Game` table (via `UpdateGameDataForYearAndWeekAsync`)
- **Then runs:** `SetSOS → CalculatePowerRatings` (does NOT call `UpdateTeamRecordsAsync`
  or `CalculateRankings` — potential gap vs `updateWeekGamesFromFile`)
- **Notes:** Previous data source blacklisted the server after bulk historical scrape.

#### `GET listAvailableFiles`
Lists all `.txt` files available in the `NCAA Raw Game Data` directory.
- **Params:** None
- **Returns:** File names available for `processSingleFile` or `updateWeekGamesFromFile`

---

### Metrics Calculation

#### `POST updateTeamRecords`
Recalculates wins, losses, and points totals for all teams from raw `Game` data.
- **Params:** `year` (optional, defaults to current year)
- **Writes:** `TeamRecords`
- **Notes:** Should be run before `setSOS` if game data has changed.

#### `POST setSOS`
Calculates and writes `BaseSOS`, `SubSOS`, and `CombinedSOS` for all teams.
- **Params:** `year` (optional), `week` (optional, defaults to 0)
- **Writes:** `TeamRecords`
- **Week behavior:**
  - Week 0: seeds from 10-year projected wins (preseason initialization)
  - Weeks 1-5: no-op (too early for meaningful SOS)
  - Week 6+: uses current year wins/losses

#### `GET calculatePowerRatings`
Computes per-game Z-scores and writes `PowerRating` for all FBS teams.
- **Params:** `year` (optional, defaults to current year)
- **Writes:** `TeamRecords`
- **Requires:** `CombinedSOS` to be current in `TeamRecords` (run `setSOS` first)

#### `GET calculateRankings`
Computes `Ranking = WinPct × CombinedSOS × (1 + PowerRating)` for all FBS teams.
- **Params:** `year` (optional, defaults to current year)
- **Writes:** `TeamRecords`
- **Requires:** `PowerRating` and `CombinedSOS` to be current (run `calculatePowerRatings` first)

#### `POST updateWeeklyMetrics`
Runs `SetSOS → CalculatePowerRatings → CalculateRankings` in one call without loading
new game data. Use after manually correcting data or to force a recalculation.
- **Params:** `year` (optional), `week` (optional)
- **Writes:** `TeamRecords`

#### `POST backfillAllMetrics`
Runs `SetSOS → CalculatePowerRatings → CalculateRankings` for every year in
`TeamRecords`. Use to rebuild all derived metrics after schema changes or algorithm
updates.
- **Params:** `startYear` (optional, to limit range)
- **Writes:** `TeamRecords`

#### `POST computeWeeklyRankings`
Computes and saves a `WeeklyRankings` snapshot for a specific year/week, including all
six offense/defense metrics. Duplicate of `ProductionGameDataController.computeweekly`
— candidate for retirement once gateway pattern is implemented.
- **Params:** `year` (required), `week` (required)
- **Writes:** `WeeklyRankings`

#### `POST backfillWeeklyRankings`
Computes and saves `WeeklyRankings` snapshots for every year/week combination that has
played games. Run after truncating `WeeklyRankings` or after algorithm changes.
- **Params:** `startYear` (optional)
- **Writes:** `WeeklyRankings`
- **Notes:** Can take significant time — 60 years × ~15 weeks × all FBS teams.
  Start with `startYear=2024` to validate before running full history.

---

### Score Deltas and Rivalry Data

#### `POST recalculateScoreDeltas`
Incrementally recalculates `AvgScoreDeltas` from all game history, upserting changed
buckets without clearing the table first.
- **Params:** None
- **Writes:** `AvgScoreDeltas`
- **When to run:** After loading a new season's complete data.

#### `POST recreateAvgScoreDeltasTable`
Clears `AvgScoreDeltas` entirely and rebuilds from scratch.
- **Params:** None
- **Writes:** `AvgScoreDeltas` (full truncate and repopulate)
- **When to run:** After major algorithm changes to the bucket calculation, or if the
  table is suspected to be corrupted.

#### `POST calculateMatchupHistories`
Computes rivalry-specific variance data for all 50 curated rivalries across four tiers
(EPIC, NATIONAL, STATE, MEH). Clears and repopulates `MatchupHistory`.
- **Params:** None
- **Writes:** `MatchupHistory`
- **When to run:** Once after initial data load, then only when rivalry seed data changes.

---

### Analytics and Diagnostics

#### `GET analytics`
Returns the top 10 over-performers and under-performers across all seasons or a year
range, measured by actual wins vs CombinedSOS-predicted wins.
- **Params:** `startYear` (optional), `endYear` (optional)
- **Reads:** `TeamRecords`
- **Returns:** Overperformers, underperformers, average PowerRating and SOS

#### `GET analyzeTeamGames`
Shows a game-by-game breakdown for a specific team including expected margin, actual
margin, home field adjustment, Z-score, and performance label for every game.
Useful for validating the algorithm against known outcomes.
- **Params:** `teamId` (required), `year` (optional)
- **Reads:** `Game`, `TeamRecords`, `AvgScoreDeltas`
- **Returns:** Per-game analysis plus season summary (avgZScore, PowerRating, CombinedSOS)

#### `GET calculateTrends`
Returns a snapshot trend label (Ascending/Stable/Descending) based on each team's
current `PowerRating`. Currently a thin wrapper — full trend analysis using
`WeeklyRankings` history is planned.
- **Params:** `teamId` (optional), `year` (optional)
- **Reads:** `TeamRecords`
- **Notes:** Placeholder implementation. Full week-over-week trajectory and historical
  comparison against prior seasons is on the roadmap.

#### `GET offenseDefenseRankings`
Returns offense and defense rankings from `WeeklyRankings` for a given year/week.
Supports filtering by team and sorting by offense, defense, or overall rank.
- **Params:** `year` (optional), `week` (optional, defaults to latest week with data),
  `teamId` (optional), `sortBy` (optional: "offense" | "defense" | "overall")
- **Reads:** `WeeklyRankings`

#### `GET diagnosticScoreDeltas`
Validates the upset handling logic in score delta calculations. Identifies games where
the lower-win-percentage team won and confirms negative deltas are correctly represented.
- **Params:** `year` (optional)
- **Reads:** `Game`, `TeamRecords`
- **Returns:** Upset count, negative delta count, and 20 sample games with explanations

---

## ProductionGameDataController
**Route prefix:** `/api/productiongamedata`
**Purpose:** Serves pre-computed data to the mobile app.
**Intended client:** .NET MAUI mobile app.
**Auth requirement:** Subscriber role / pay wall (to be implemented).

---

### Rankings

#### `GET powerrankings`
The primary rankings endpoint. Returns all FBS teams ranked for a given year.
When `throughWeek` is provided, returns the pre-computed `WeeklyRankings` snapshot
for that week. When omitted, returns final season data from `TeamRecords`.
- **Params:** `year` (optional), `throughWeek` (optional)
- **Reads:** `WeeklyRankings` (week-specific) or `TeamRecords` (final season)
- **Returns:** TeamID, TeamName, Conference, Tier, Record, Ranking, PowerRating,
  CombinedSOS, OverallRank, TierRank, AvgPointsScored, AvgPointsAllowed,
  OffensiveZScore, DefensiveZScore, OffensiveRank, DefensiveRank

#### `GET queryTeamRecords`
Flexible query endpoint for filtering team records by year range, wins, losses,
power rating range, etc.
- **Params:** `wins`, `losses`, `minWins`, `maxWins`, `startYear`, `endYear`,
  `minPowerRating`, `maxPowerRating` (all optional)
- **Reads:** `TeamRecords`

---

### Predictions

#### `GET predictMatchup`
Predicts the outcome of a single matchup between two named teams.
- **Params:** `teamName`, `opponentName`, `location`, `week`
- **Reads:** `TeamRecords`, `AvgScoreDeltas`, `MatchupHistory`
- **Returns:** Projected scores, expected margin, over/under, win probability

#### `POST predictMatchups`
Batch version of `predictMatchup`. Accepts a list of matchup requests.
- **Body:** Array of `MatchupRequest` objects
- **Reads:** `TeamRecords`, `AvgScoreDeltas`, `MatchupHistory`

---

### Schedule

#### `GET schedule`
Returns the full schedule for a season with actual scores for played games and projected
scores for unplayed games.
- **Params:** `year` (optional)
- **Reads:** `Game`, `TeamRecords`, `AvgScoreDeltas`
- **Returns:** Every game with actual or projected scores, over/under, and team metadata

---

### Teams and Rivalries

#### `GET teams`
Returns all teams with metadata.
- **Params:** None
- **Reads:** `Team`

#### `GET rivalries`
Returns all rivalry matchups with history.
- **Params:** `teamId` (optional)
- **Reads:** `MatchupHistory`, `Team`

#### `GET rivalries/named`
Returns rivalries filtered by tier (EPIC, NATIONAL, STATE, MEH).
- **Params:** `tier` (optional)
- **Reads:** `MatchupHistory`, `Team`

#### `GET rivalryhistory`
Returns detailed head-to-head game history for a specific rivalry.
- **Params:** `team1Id`, `team2Id`
- **Reads:** `Game`, `MatchupHistory`

#### `GET teamhistory`
Returns season-by-season history for a specific team.
- **Params:** `teamId`
- **Reads:** `TeamRecords`

---

### Conference Standings and Projections

#### `GET championship-qualifiers`
Returns current conference championship qualifiers based on actual results to date.
- **Params:** `year` (optional)
- **Reads:** `TeamRecords`, `Game`, `Team`

#### `GET projected-championship-qualifiers`
Returns projected conference championship qualifiers based on predicted outcomes for
remaining games.
- **Params:** `year` (optional)
- **Reads:** `Game`, `TeamRecords`, `ProjectionCacheService`

#### `GET projected-standings`
Returns full projected final standings for all conferences based on predicted outcomes.
- **Params:** `year` (optional)
- **Reads:** `Game`, `TeamRecords`, `ProjectionCacheService`

---

### Administration (Misplaced — migrate to DeveloperController)

#### `POST computeweekly`
Computes and saves `WeeklyRankings` for a specific year/week, or backfills an entire
year. Identical in function to `DeveloperController.computeWeeklyRankings` and
`backfillWeeklyRankings`. Should be moved to `DeveloperController` once the gateway
pattern is implemented.
- **Params:** `year` (optional), `week` (required unless `backfill=true`),
  `backfill` (optional bool)
- **Writes:** `WeeklyRankings`

---

### Diagnostics

#### `GET diagnostic`
Returns a health summary of `TeamRecords` — total records, records with PowerRating,
year range, and per-year game counts.
- **Params:** None
- **Reads:** `TeamRecords`

---

## Recommended Weekly Update Sequence

When a week's games are finalized, run these endpoints in order:

```
1. POST updateWeekGamesFromFile?year=YYYY&week=WW
   (loads games + runs full TeamRecords metrics pipeline)

2. POST computeWeeklyRankings?year=YYYY&week=WW
   (writes WeeklyRankings snapshot with offense/defense metrics)
```

That's it. Two calls covers everything the mobile app needs for that week.

---

## Recommended Full Rebuild Sequence

When rebuilding from scratch or after major algorithm changes:

```
1. POST loadGameHistoryFromFiles          (load all raw game data)
2. POST recreateAvgScoreDeltasTable       (rebuild historical expectation buckets)
3. POST calculateMatchupHistories         (rebuild rivalry variance data)
4. POST backfillAllMetrics                (rebuild TeamRecords metrics for all years)
5. POST backfillWeeklyRankings            (rebuild all WeeklyRankings snapshots)
```
