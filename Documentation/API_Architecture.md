# NCAA Power Ratings — API Architecture

## Overview

The backend is an ASP.NET Core Web API with two controllers, a set of focused services,
and a SQL Server database accessed via Entity Framework Core. The system ingests raw
college football game data, computes a suite of power rating metrics, and serves that
data to a .NET MAUI mobile app.

---

## Controller Responsibilities

### ProductionGameDataController
**Route:** `/api/productiongamedata`

The mobile-facing API. All endpoints are read-only (GET) except `computeweekly` which is
an administrative action that currently lives here but should migrate to
`DeveloperController` once the gateway pattern is implemented. No endpoints in this
controller modify raw game data or recalculate core metrics — they only read and present
pre-computed data.

### DeveloperController
**Route:** `/api/developer`

The administration API. Handles all data loading, metric recalculation, backfill
operations, and diagnostics. Intended for use from a web-based admin console once the
app goes live. All state-modifying operations live here. These endpoints should be
protected by admin-only authentication before the app is publicly accessible.

---

## Service Layer

### GameDataService (`IGameDataService`)
Responsible for raw game data ingestion. Reads from local text files or web sources,
parses game records using `ColumnMap` for year-specific column layouts, and writes to
the `Game` table. Also maintains `TeamRecords` win/loss/points totals via
`UpdateTeamRecordsAsync` after each game load.

### TeamMetricsService
Computes all derived metrics in sequence. Must always be called in pipeline order:

```
SetSOS → CalculatePowerRatings → CalculateRankings
```

Reads from `Game` and `TeamRecords`, writes back to `TeamRecords`. See
`TeamMetrics_Documentation.md` for full calculation details.

### WeeklyRankingsService
Runs the same metric pipeline as `TeamMetricsService` but scoped to games played through
a specific week, writing point-in-time snapshots to `WeeklyRankings`. Also computes and
stores `OffensiveZScore`, `DefensiveZScore`, `OffensiveRank`, and `DefensiveRank` which
`TeamMetricsService` does not yet produce. Called after each week's games are finalized,
and by the backfill pipeline for historical data.

### GamePredictionService
Predicts game outcomes for unplayed games. Uses each team's current `TeamRecords`
(PPG, PAG, PowerRating, CombinedSOS) as inputs alongside the `AvgScoreDeltas` historical
expectation table. Called by `ProjectionCacheService` and directly by prediction
endpoints.

### ProjectionCacheService (Singleton)
Caches game predictions for an entire season keyed by `(year, gameId)`. Prevents
redundant prediction recalculation across multiple endpoint calls within the same
request cycle. Cache is invalidated when the year changes or explicitly via `Invalidate()`.

### ConferenceChampionshipService
Determines conference championship qualifiers and projected qualifiers based on current
or projected standings. Used by the championship-related endpoints.

### ScoreDeltaCalculator
Rebuilds the `AvgScoreDeltas` lookup table from all historical game data. Groups games
by win-percentage bucket pairs (5% increments) and computes average margin and standard
deviation for each bucket. Should be rerun any time significant new historical data is
loaded.

### MatchupHistoryCalculator
Computes rivalry-specific variance data for the 50 curated rivalries across four tiers
(EPIC, NATIONAL, STATE, MEH). Populates `MatchupHistory` with average margin, standard
deviation, upset rate, and game count. Used by `TeamMetricsService` and
`WeeklyRankingsService` to widen the expected variance for historically unpredictable
matchups.

---

## Data Flow

### Weekly update flow (in-season):
```
New week's games available
        ↓
updateWeekGamesFromFile OR updateWeekGames
        ↓
GameDataService.UpdateGameDataFromFileAsync / UpdateGameDataForYearAndWeekAsync
        ↓
GameDataService.UpdateTeamRecordsAsync  →  writes wins/losses/points to TeamRecords
        ↓
TeamMetricsService.SetSOS               →  writes BaseSOS/SubSOS/CombinedSOS to TeamRecords
        ↓
TeamMetricsService.CalculatePowerRatings →  writes PowerRating to TeamRecords
        ↓
TeamMetricsService.CalculateRankings    →  writes Ranking to TeamRecords
        ↓
WeeklyRankingsService.ComputeAndSaveAsync →  writes full snapshot to WeeklyRankings
```

### Mobile app read flow:
```
Mobile app requests rankings for year=2025, week=13
        ↓
ProductionGameDataController.powerrankings
        ↓
Queries WeeklyRankings WHERE Year=2025 AND Week=13
        ↓
Returns pre-computed snapshot — no calculation at query time
```

### Prediction flow:
```
Mobile app requests schedule with projections
        ↓
ProductionGameDataController.schedule
        ↓
ProjectionCacheService.GetAllProjections (builds cache if needed)
        ↓
GamePredictionService.PredictMatchups
        ↓
Reads TeamRecords for PPG/PAG/PowerRating baseline
        ↓
Looks up AvgScoreDeltas for win-pct-based expected margin
        ↓
Returns projected score, margin, and over/under
```

---

## Database Tables

| Table | Written by | Read by | Purpose |
|---|---|---|---|
| `Game` | GameDataService | All services | Raw game results |
| `Team` | Seed data | All services | Team metadata, division, conference |
| `TeamRecords` | GameDataService, TeamMetricsService | GamePredictionService, all controllers | Current season stats and metrics per team |
| `WeeklyRankings` | WeeklyRankingsService | ProductionGameDataController, DeveloperController | Point-in-time weekly snapshots |
| `AvgScoreDeltas` | ScoreDeltaCalculator | TeamMetricsService, WeeklyRankingsService, GamePredictionService, DeveloperController | Historical expected margin lookup by win-pct bucket |
| `MatchupHistory` | MatchupHistoryCalculator | TeamMetricsService, WeeklyRankingsService | Rivalry-specific variance multipliers |

---

## Future Architecture: API Gateway Pattern

The current design has some logic duplication between the two controllers and `computeweekly`
sits in the wrong controller. The planned evolution is:

```
MobileApp     →  ProductionGameDataController  ──┐
                                                   ├──  ApiGateway  ──  Services
AdminConsole  →  DeveloperController           ──┘
```

`ApiGateway` becomes a single injectable service containing all endpoint logic. Both
controllers become thin routing layers that handle authentication, authorization, and
rate limiting appropriate to their audience, then delegate to the gateway.

**Benefits:**
- No duplicated logic
- Auth concerns separated from business logic
- New clients (web app, third-party API) add a new thin controller only
- Unit testing the gateway covers all clients simultaneously

**Prerequisites before implementing:**
- ASP.NET Core Identity for user management
- JWT Bearer token authentication
- Role-based authorization (`[Authorize(Roles = "Admin")]` on DeveloperController)
- Stripe or equivalent for subscription/pay wall on Production endpoints

---

## Planned Enhancements

- **Early season seeding** — weeks 1-6 use 10-year rolling average baselines per team
  instead of sparse current-season data. `FifoDoubleQueue` is already built for this.
- **Trends feature** — week-over-week trajectory using `WeeklyRankings` history, blended
  with historical season patterns from `TeamRecords`
- **Offensive/Defensive metrics in TeamRecords** — six new columns added; population via
  `CalculatePowerRatings` extension pending
- **Rolling average preseason ratings** — project full season before week 1 using
  10-year rolling averages for PPG, PAG, and PowerRating
