# NCAA Power Ratings — Admin Operations Guide

## Overview

This document covers the routine weekly tasks to keep the app current during the season,
the full rebuild procedure for emergency data recovery, and notes on data sources.
All admin operations are performed through the Developer Controller endpoints, accessible
via Swagger at `/swagger` when the API is running.

---

## Routine Weekly Update

Run these two steps after each week's games are finalized (typically Sunday morning):

### Step 1 — Load game results
```
POST /api/developer/updateWeekGamesFromFile?year=YYYY&week=WW
```
Loads the week's game results from the local data file, updates `TeamRecords` wins/losses/
points, and recalculates SOS, PowerRating, and Ranking for all FBS teams.

### Step 2 — Compute weekly snapshot
```
POST /api/developer/computeweekly?year=YYYY&week=WW
```
Writes the full `WeeklyRankings` snapshot for that week including Offensive and Defensive
Index rankings. This is what the mobile app reads when a user selects that week.

**That's it.** Two calls covers everything the mobile app needs for the week.

---

## Start of Season Setup

Run once before the first game of each new season:

### 1. Seed preseason rankings
```
POST /api/developer/setSOS?year=YYYY&week=0
```
Week 0 triggers the preseason seed — uses the 10-year rolling average to initialize
`TeamRecords` before any games are played. This gives the projection engine meaningful
baseline data from day one.

### 2. Verify team data
```
GET /api/developer/listAvailableFiles
```
Confirm the current season's data file is present in the `NCAA Raw Game Data` directory.

---

## After Algorithm Changes

Any time the core calculation logic changes (Z-score formula, SOS weighting, rivalry
multipliers, etc.), historical data needs to be rebuilt to reflect the new algorithm.

### 1. Rebuild score delta lookup table
```
POST /api/developer/recreateAvgScoreDeltasTable
```
Full truncate and rebuild of `AvgScoreDeltas` from all historical game data. Run this
first — everything else depends on it.

### 2. Rebuild rivalry variance data
```
POST /api/developer/calculateMatchupHistories
```
Rebuilds rivalry-specific variance multipliers for all 50 curated rivalries.

### 3. Rebuild TeamRecords metrics
```
POST /api/developer/backfillAllMetrics?startYear=1965
```
Recalculates SOS, PowerRating, and Ranking in `TeamRecords` for every year.
This can take several minutes.

### 4. Rebuild WeeklyRankings snapshots
```
POST /api/developer/computeweekly?year=YYYY&backfill=true
```
Run once per year you want to rebuild. Start with the current year and work backwards
as needed. Each year backfill runs all weeks for that year.

---

## Emergency Full Rebuild

If the database is lost, corrupted, or needs to be recreated from scratch, run these
steps in order. Do not skip steps or change the order — each step depends on the
previous one.

### Step 1 — Restore or recreate the database schema
Restore from backup if available. If not, run the EF Core migrations to recreate
the schema:
```bash
dotnet ef database update
```
Then manually run the column additions for the six offense/defense fields:
```sql
-- WeeklyRankings
ALTER TABLE WeeklyRankings ADD AvgPointsScored  DECIMAL(5,2) NOT NULL DEFAULT 0;
ALTER TABLE WeeklyRankings ADD AvgPointsAllowed DECIMAL(5,2) NOT NULL DEFAULT 0;
ALTER TABLE WeeklyRankings ADD OffensiveZScore  DECIMAL(7,4) NOT NULL DEFAULT 0;
ALTER TABLE WeeklyRankings ADD DefensiveZScore  DECIMAL(7,4) NOT NULL DEFAULT 0;
ALTER TABLE WeeklyRankings ADD OffensiveRank    INT NOT NULL DEFAULT 0;
ALTER TABLE WeeklyRankings ADD DefensiveRank    INT NOT NULL DEFAULT 0;

-- TeamRecords
ALTER TABLE TeamRecords ADD AvgPointsScored  DECIMAL(5,2) NOT NULL DEFAULT 0;
ALTER TABLE TeamRecords ADD AvgPointsAllowed DECIMAL(5,2) NOT NULL DEFAULT 0;
ALTER TABLE TeamRecords ADD OffensiveZScore  DECIMAL(7,4) NOT NULL DEFAULT 0;
ALTER TABLE TeamRecords ADD DefensiveZScore  DECIMAL(7,4) NOT NULL DEFAULT 0;
ALTER TABLE TeamRecords ADD OffensiveRank    INT NOT NULL DEFAULT 0;
ALTER TABLE TeamRecords ADD DefensiveRank    INT NOT NULL DEFAULT 0;
```

### Step 2 — Load all historical game data
```
GET /api/developer/loadGameHistoryFromFiles
```
Loads all `.txt` files from the `NCAA Raw Game Data` directory into the `Game` table.
Covers 1965 to present. This is the longest step — expect several minutes.

### Step 3 — Populate TeamRecords from game data
```
POST /api/developer/updateTeamRecords
```
Calculates wins, losses, and points totals for all teams across all years from the
raw `Game` data.

### Step 4 — Rebuild score delta lookup table
```
POST /api/developer/recreateAvgScoreDeltasTable
```
Builds the historical expectation model from all game data. Must run after games
are loaded and before any metric calculation.

### Step 5 — Rebuild rivalry variance data
```
POST /api/developer/calculateMatchupHistories
```
Populates rivalry-specific variance multipliers. Must run before power ratings.

### Step 6 — Rebuild all metrics
```
POST /api/developer/backfillAllMetrics?startYear=1965
```
Calculates SOS, PowerRating, and Ranking for all teams across all years.
Expect 5-10 minutes for the full history.

### Step 7 — Rebuild WeeklyRankings snapshots
Run once per year from 1965 to present:
```
POST /api/developer/computeweekly?year=1965&backfill=true
POST /api/developer/computeweekly?year=1966&backfill=true
... (continue through current year)
```
This is the most time-consuming step. Consider scripting it.
Alternatively, start from a recent year if full history is not critical:
```
POST /api/developer/computeweekly?year=2010&backfill=true
```

### Step 8 — Verify
```
GET /api/developer/diagnostic
GET /api/developer/offenseDefenseRankings?year=2025&week=15&sortBy=overall
```
Confirm row counts look correct and rankings for the most recent completed season
look sensible.

---

## Diagnostic Endpoints

Use these to verify data integrity at any time:

| Endpoint | Purpose |
|---|---|
| `GET /api/developer/diagnostic` | Row counts, year range, records with PowerRating |
| `GET /api/developer/analyzeTeamGames?teamId=110&year=2025` | Game-by-game Z-score breakdown for Texas 2025 — good sanity check |
| `GET /api/developer/offenseDefenseRankings?year=2025&week=15` | Full offense/defense rankings for end of 2025 regular season |
| `GET /api/developer/diagnosticScoreDeltas?year=2025` | Validates upset handling in score delta calculations |
| `GET /api/productiongamedata/diagnostic` | TeamRecords health summary |

**Sanity checks after a rebuild:**
- Indiana should appear near the top for 2025 (16-0 season)
- Georgia should rank above Alabama at week 16 of 2025 (Georgia won the SEC Championship)
- Texas should show slightly negative Offensive and Defensive Index for 2025
  (underperformed expectations on both sides of the ball)

---

## Raw Data Files

### Current format
Game data lives in `/NCAA Raw Game Data/` as yearly `.txt` files (e.g. `2024.txt`,
`2025.txt`). The format changed in 2013 — `ColumnMap.cs` handles both layouts
automatically.

**Pre-2013 columns:** Rk, Wk, Date, Day, Winner, Pts, Location, Loser, Pts, Notes

**2013+ columns:** Rk, Wk, Date, Time, Day, Winner, Pts, Location, Loser, Pts, Notes

### Data source status
The original web data source blacklisted the server after a full 60-year bulk scrape
in one session. The `updateWeekGames` endpoint (live web fetch) exists but should be
used cautiously until a reliable replacement source is confirmed.

**Current approach:** Manual file download and `updateWeekGamesFromFile` for weekly
updates during the season.

### Candidate replacement sources
When evaluating a new data source, look for:
- Full historical game results back to at least 2000 (ideally 1965)
- Winner, loser, points scored, game location (home/away/neutral), week number
- Reliable API with rate limits that won't flag normal weekly usage
- Free tier sufficient for weekly in-season updates

**Known options to evaluate:**
- **College Football Data API (CFBD)** — `collegefootballdata.com/api` — free tier
  with API key, good historical coverage, widely used in the CFB analytics community.
  Strong candidate.
- **ESPN hidden API** — undocumented but widely used. No guarantee of stability.
- **Sports Reference / CFB Reference** — comprehensive historical data but scraping
  their site risks the same blacklisting issue as before. Check if they offer a
  data export or API.
- **Sportradar** — commercial, reliable, but expensive for a solo project.

**Recommendation:** Start with College Football Data API. It has the historical depth
you need, a proper API key system that won't flag normal usage, and an active community
that documents the endpoints.

---

## File Locations

| What | Where |
|---|---|
| Raw game data files | `NCAA Raw Game Data/*.txt` |
| SQLite database | `NCAA_Power_Ratings/ncaa-rankings.db` |
| API project | `NCAA_Power_Ratings/` |
| Mobile app project | `NCAA_Power_Ratings.Mobile/` |
| Documentation | `/docs/` (solution root) |

---

## Notes

- The database file `ncaa-rankings.db` should be backed up before any major rebuild.
  Copy it to a safe location first — the rebuild is not reversible without the backup.
- `WeeklyRankings` can be truncated and rebuilt at any time without touching `Game`
  or `TeamRecords`. It is a derived table — all the source data lives in `Game`.
- `TeamRecords` can similarly be rebuilt from `Game` data at any time using
  `updateTeamRecords` followed by `backfillAllMetrics`.
- The only truly irreplaceable table is `Game` — protect that data above all else.
