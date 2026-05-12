# Developer API Quick Reference

## Base URL
```
http://localhost:5086/api/developer
```

⚠️ **WARNING: These endpoints modify database state. NOT FOR PRODUCTION USE.**

---

## Data Loading Endpoints

### 1. Load Game History from Files
**GET** `/loadGameHistoryFromFiles`

Loads game data for last 60 years from text files in NCAA Raw Game Data directory.

**Example:**
```bash
curl "http://localhost:5086/api/developer/loadGameHistoryFromFiles"
```

### 2. Process Single File
**POST** `/processSingleFile?filePath={path}`

Processes a single file from the NCAA Raw Game Data directory.

**Example:**
```bash
curl -X POST "http://localhost:5086/api/developer/processSingleFile?filePath=D:\NCAA%20Raw%20Game%20Data\2024.txt"
```

### 3. Update Week Games from File ⭐ **Most Used**
**POST** `/updateWeekGamesFromFile?year={year}&week={week}`

Updates game data for a specific year and week from local file. **Automatically recalculates TeamRecords, SOS, PowerRating, and Ranking.**

**Example:**
```bash
curl -X POST "http://localhost:5086/api/developer/updateWeekGamesFromFile?year=2025&week=8"
```

### 4. Update Week Games (Web Scraping)
**POST** `/updateWeekGames?year={year}&week={week}`

Fetches fresh data from the web for a specific week.

**Example:**
```bash
curl -X POST "http://localhost:5086/api/developer/updateWeekGames?year=2024&week=10"
```

### 5. List Available Files
**GET** `/listAvailableFiles`

Lists all .txt files in the NCAA Raw Game Data directory.

**Example:**
```bash
curl "http://localhost:5086/api/developer/listAvailableFiles"
```

---

## Team Records and Metrics

### 6. Update Team Records
**POST** `/updateTeamRecords?year={year}`

Rebuilds team records for the specified year (or all years if omitted).

**Example:**
```bash
curl -X POST "http://localhost:5086/api/developer/updateTeamRecords?year=2024"
```

### 7. Calculate SOS
**POST** `/setSOS?year={year}&week={week}`

Calculates Strength of Schedule (BaseSOS, SubSOS, CombinedSOS).

**Example:**
```bash
curl -X POST "http://localhost:5086/api/developer/setSOS?year=2024&week=10"
```

### 8. Calculate Power Ratings
**GET** `/calculatePowerRatings?year={year}`

Calculates power ratings for all teams.

**Example:**
```bash
curl "http://localhost:5086/api/developer/calculatePowerRatings?year=2024"
```

### 9. Calculate Rankings
**GET** `/calculateRankings?year={year}`

Calculates rankings using the formula: `WinPct × CombinedSOS × (1 + PowerRating)`

**Example:**
```bash
curl "http://localhost:5086/api/developer/calculateRankings?year=2024"
```

### 10. Update Weekly Metrics ⭐ **Workflow**
**POST** `/updateWeeklyMetrics?year={year}&week={week}`

Runs full weekly metric calculation: SOS → PowerRating → Ranking

**Example:**
```bash
curl -X POST "http://localhost:5086/api/developer/updateWeeklyMetrics?year=2024&week=10"
```

### 11. Backfill All Metrics ⭐ **Full Recalc**
**POST** `/backfillAllMetrics?startYear={year}`

Recalculates all metrics for all years (or from startYear forward).

**Example:**
```bash
curl -X POST "http://localhost:5086/api/developer/backfillAllMetrics?startYear=2020"
```

---

## Score Delta and Rivalry Calculations

### 12. Recalculate Score Deltas
**POST** `/recalculateScoreDeltas`

Recalculates the AvgScoreDeltas table using 5% win-percentage buckets.

**Example:**
```bash
curl -X POST "http://localhost:5086/api/developer/recalculateScoreDeltas"
```

### 13. Recreate Score Deltas Table
**POST** `/recreateAvgScoreDeltasTable`

Drops all data from AvgScoreDeltas and recalculates from scratch.

**Example:**
```bash
curl -X POST "http://localhost:5086/api/developer/recreateAvgScoreDeltasTable"
```

### 14. Calculate Matchup Histories
**POST** `/calculateMatchupHistories`

Calculates matchup histories for all 50 curated rivalries.

**Example:**
```bash
curl -X POST "http://localhost:5086/api/developer/calculateMatchupHistories"
```

---

## Analytics and Diagnostics

### 15. Get Analytics
**GET** `/analytics?startYear={year}&endYear={year}`

Provides detailed analytics on team performance vs calculated metrics.

**Example:**
```bash
curl "http://localhost:5086/api/developer/analytics?startYear=2020&endYear=2024"
```

**Response:**
```json
{
  "totalRecords": 1523,
  "yearRange": "2020-2024",
  "overperformers": [
    {
      "year": 2024,
      "teamName": "Indiana",
      "record": "11-1",
      "combinedSOS": 0.4234,
      "powerRating": 0.0189,
      "overperformance": 5.92
    }
  ],
  "underperformers": [...],
  "averagePowerRating": 0.0012,
  "averageSOS": 0.5123
}
```

### 16. Analyze Team Games
**GET** `/analyzeTeamGames?teamId={id}&year={year}`

Shows detailed game-by-game analysis for a specific team including Z-scores and performance evaluation.

**Example:**
```bash
curl "http://localhost:5086/api/developer/analyzeTeamGames?teamId=110&year=2024"
```

**Response:**
```json
{
  "teamId": 110,
  "year": 2024,
  "record": "11-1",
  "combinedSOS": 0.5234,
  "avgZScore": 1.2345,
  "powerRating": 0.0234,
  "calculatedPowerRating": 0.0646,
  "games": [
    {
      "week": 1,
      "opponentName": "Indiana State",
      "location": "Home",
      "result": "W",
      "delta": 31,
      "teamFinalWins": 11,
      "oppFinalWins": 7,
      "baseExpectedDelta": 8.5,
      "homeAdjustment": 2.5,
      "adjustedExpectedDelta": 11.0,
      "actualDelta": 31,
      "difference": 20.0,
      "zScore": 1.567,
      "performance": "Dominant"
    }
  ]
}
```

### 17. Calculate Trends ⭐ **Front-End Candidate**
**GET** `/calculateTrends?teamId={id}&year={year}`

Calculates trend projections based on recent performance. Can be filtered by team or show all teams.

**Example:**
```bash
# All teams
curl "http://localhost:5086/api/developer/calculateTrends?year=2024"

# Single team
curl "http://localhost:5086/api/developer/calculateTrends?teamId=110&year=2024"
```

**Response:**
```json
{
  "year": 2024,
  "teamCount": 133,
  "trends": [
    {
      "teamId": 110,
      "teamName": "Ohio State",
      "year": 2024,
      "record": "11-1",
      "powerRating": 0.0234,
      "combinedSOS": 0.5234,
      "ranking": 0.6543,
      "winPercentage": 0.9167,
      "projectedFinalRanking": 0.6543,
      "trend": "Ascending"
    }
  ]
}
```

### 18. Diagnostic Score Deltas
**GET** `/diagnosticScoreDeltas?year={year}`

Verifies score delta calculations and upset handling logic.

**Example:**
```bash
curl "http://localhost:5086/api/developer/diagnosticScoreDeltas?year=2024"
```

---

## Common Workflows

### Weekly Update Workflow
```bash
# 1. Load the week's games from file
curl -X POST "http://localhost:5086/api/developer/updateWeekGamesFromFile?year=2025&week=8"

# Done! The above automatically:
# - Loads game data
# - Updates TeamRecords
# - Calculates SOS
# - Calculates PowerRating
# - Calculates Ranking
```

### Full System Reset
```bash
# 1. Clear and rebuild score deltas
curl -X POST "http://localhost:5086/api/developer/recreateAvgScoreDeltasTable"

# 2. Rebuild matchup histories
curl -X POST "http://localhost:5086/api/developer/calculateMatchupHistories"

# 3. Backfill all metrics
curl -X POST "http://localhost:5086/api/developer/backfillAllMetrics"
```

### Analyze a Team's Season
```bash
# 1. Get detailed game-by-game analysis
curl "http://localhost:5086/api/developer/analyzeTeamGames?teamId=110&year=2024"

# 2. Get trend projection
curl "http://localhost:5086/api/developer/calculateTrends?teamId=110&year=2024"
```

---

## Endpoint Summary by Category

### 🔄 Data Loading (5 endpoints)
- `loadGameHistoryFromFiles` - Bulk file load
- `processSingleFile` - Single file processing
- `updateWeekGamesFromFile` ⭐ - Weekly file update (auto-recalc)
- `updateWeekGames` - Web scraping
- `listAvailableFiles` - File inventory

### 📊 Metrics (6 endpoints)
- `updateTeamRecords` - Rebuild records
- `setSOS` - Calculate SOS
- `calculatePowerRatings` - Calculate PR
- `calculateRankings` - Calculate rankings
- `updateWeeklyMetrics` ⭐ - Full weekly update
- `backfillAllMetrics` ⭐ - Full system recalc

### 🎯 Score Deltas & Rivalries (3 endpoints)
- `recalculateScoreDeltas` - Update deltas
- `recreateAvgScoreDeltasTable` - Rebuild from scratch
- `calculateMatchupHistories` - Rivalry stats

### 📈 Analytics (4 endpoints)
- `analytics` - System-wide analytics
- `analyzeTeamGames` - Team game analysis
- `calculateTrends` ⭐ - Trend projections (front-end candidate)
- `diagnosticScoreDeltas` - Delta diagnostics

**Total: 18 endpoints**

---

## Notes
- All endpoints return JSON
- Most POST endpoints trigger database modifications
- Use Swagger UI at `http://localhost:5086/swagger` for interactive testing
- The `calculateTrends` endpoint is a strong candidate for production front-end use
