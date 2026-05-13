# Quick Command Reference Card

## Most-Used Developer Endpoints

### Weekly Data Load (One-Step)
```bash
# Load week and auto-recalc everything
POST /api/developer/updateWeekGamesFromFile?year=2025&week=8
```

### Check What's Available
```bash
# List data files
GET /api/developer/listAvailableFiles

# System analytics
GET /api/developer/analytics?startYear=2020&endYear=2024
```

### Team Analysis
```bash
# Game-by-game breakdown
GET /api/developer/analyzeTeamGames?teamId=110&year=2024

# Trends and projections (⭐ front-end candidate)
GET /api/developer/calculateTrends?teamId=110&year=2024
GET /api/developer/calculateTrends?year=2024  # all teams
```

### Manual Metric Recalc
```bash
# Full weekly metrics
POST /api/developer/updateWeeklyMetrics?year=2024&week=10

# Individual metrics
POST /api/developer/setSOS?year=2024&week=10
GET  /api/developer/calculatePowerRatings?year=2024
GET  /api/developer/calculateRankings?year=2024
```

### System Maintenance
```bash
# Rebuild score deltas
POST /api/developer/recalculateScoreDeltas

# Rebuild matchup histories
POST /api/developer/calculateMatchupHistories

# Full system recalc
POST /api/developer/backfillAllMetrics?startYear=2020
```

---

## Production Endpoints

### Predictions
```bash
# Single game
GET /api/productiongamedata/predictMatchup?year=2025&teamName=Ohio State&opponentName=Michigan&location=H&week=12

# Batch
POST /api/productiongamedata/predictMatchups
Body: {"year":2025,"matchups":[{"teamName":"Ohio State","opponentName":"Michigan","location":"H","week":12}]}
```

### Queries
```bash
# Team records
GET /api/productiongamedata/queryTeamRecords?startYear=2020&endYear=2024&minWins=10

# Rivalries
GET /api/productiongamedata/rivalries?tier=EPIC
```

---

## PowerShell Quick Commands

```powershell
# Base URL
$base = "http://localhost:5086/api"

# Load week 8
Invoke-RestMethod -Uri "$base/developer/updateWeekGamesFromFile?year=2025&week=8" -Method Post

# Get trends
Invoke-RestMethod -Uri "$base/developer/calculateTrends?year=2024" -Method Get | ConvertTo-Json -Depth 10

# Predict game
Invoke-RestMethod -Uri "$base/productiongamedata/predictMatchup?year=2025&teamName=Texas&opponentName=Michigan&location=N&week=20" -Method Get

# Get analytics
Invoke-RestMethod -Uri "$base/developer/analytics?startYear=2020" -Method Get | ConvertTo-Json -Depth 10
```

---

## Swagger UI
```
http://localhost:5086/swagger
```

**Controllers:**
- **Developer** (18 endpoints) - Admin/debug operations
- **ProductionGameData** (4 endpoints) - Production API

---

## Front-End Candidate Evaluation

Test **calculateTrends** to see if it's useful for front-end:

```bash
curl "http://localhost:5086/api/developer/calculateTrends?year=2024"
```

**If you like it:**
1. Consider moving to ProductionGameDataController
2. Add any additional filtering/sorting
3. Document as production endpoint

**Response includes:**
- Team name and ID
- Current record
- Power rating
- Combined SOS
- Ranking
- Win percentage
- Trend direction (Ascending/Stable/Descending)
- Projected final ranking
