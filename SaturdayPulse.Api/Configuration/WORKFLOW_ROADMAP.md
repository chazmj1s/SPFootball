# NCAA Power Ratings - Complete Workflow Roadmap

## 🎯 Overview

This system calculates team rankings and predicts game scores for college football. The workflow is **sequential** - you must complete steps in order.

---

## 📋 Complete Workflow (Season-Long)

### **Phase 1: Initial Setup (One-Time)**

#### 1️⃣ Load Historical Data (Optional - if starting fresh)
```
GET /api/gamedata/loadGameHistoryFromFiles
```
- **What it does**: Loads all game data from `NCAA Raw Game Data/*.txt` files
- **When to use**: First time setup or to reload all historical data
- **Output**: Populates `Game` table with all historical games

---

### **Phase 2: Weekly Operational Flow**

#### 2️⃣ Load Week's Games (ONE COMMAND!)
```
POST /api/gamedata/updateWeekGamesFromFile?year=2025&week=1
```
- **What it does**: 
  1. Loads game data for a specific week from `2025.txt`
  2. Automatically updates team records (Wins/Losses/PointsFor/PointsAgainst)
  3. Automatically calculates metrics with week-aware logic:
     - Week 0: Initialize with projected wins
     - Weeks 1-5: Calculate PR and Ranking only (SOS too early)
     - Week 6+: Full calculation including SOS
- **When to use**: After each week's games are complete and added to the file
- **Important**: This is now a **complete one-step process**!
- **Repeat**: Call once for each week (1, 2, 3, etc.)

~~#### 3️⃣ Update Team Records~~ **NO LONGER NEEDED**
~~#### 4️⃣ Calculate Weekly Metrics~~ **NO LONGER NEEDED**

These steps are now automatic when you call `updateWeekGamesFromFile`!

#### 3️⃣ Predict Next Week's Games
```
GET /api/gamedata/predictMatchup?year=2025&teamName=Texas&opponentName=Oklahoma&location=N&week=8
```
- **What it does**: Predicts score for a specific matchup
- **When to use**: After calculating metrics for the current week
- **Parameters**:
  - `location`: 'H' = home, 'A' = away, 'N' = neutral
  - `week`: The week of the predicted game
- **Output**: Predicted scores, margin, confidence interval, rivalry info

---

## 🔄 Typical Weekly Cycle

```
┌─────────────────────────────────────────────────────────────┐
│                    MONDAY (After Weekend Games)              │
├─────────────────────────────────────────────────────────────┤
│ 1. Update 2025.txt with weekend's game results              │
│ 2. POST updateWeekGamesFromFile?year=2025&week=N            │
│    ✓ Automatically loads games                              │
│    ✓ Automatically updates team records                     │
│    ✓ Automatically calculates metrics (week-aware)          │
│                                                              │
│                    TUESDAY-FRIDAY                            │
│ 3. GET predictMatchup for next week's games                 │
│    (Use for analysis, betting, fun)                         │
│                                                              │
│                    SATURDAY                                  │
│ 4. Watch games and compare predictions to actual results    │
└─────────────────────────────────────────────────────────────┘
```

---

## 🏗️ First-Time Season Setup (2025 Example)

If you're loading an entire season at once:

```powershell
# Simply load each week - everything else is automatic!
0..5 | ForEach-Object {
    $result = Invoke-RestMethod -Uri "http://localhost:5086/api/gamedata/updateWeekGamesFromFile?year=2025&week=$_" -Method POST
    Write-Host "Week $_`: $($result.metricsCalculated)" -ForegroundColor Green
}

# Now you can predict week 6+ games
$pred = Invoke-RestMethod -Uri "http://localhost:5086/api/gamedata/predictMatchup?year=2025&teamName=Texas&opponentName=Oklahoma&location=N&week=8" -Method GET
$pred | ConvertTo-Json
```

---

## 📊 Key Endpoints Reference

### **Data Loading & Processing (All-In-One)**
| Endpoint | Method | Purpose | Frequency |
|----------|--------|---------|-----------|
| `updateWeekGamesFromFile` | POST | **ONE-STEP PROCESS**: Loads games + updates records + calculates metrics | Weekly (once per week) |

### **Manual Control (Optional)**
| Endpoint | Method | Purpose | When to Use |
|----------|--------|---------|-------------|
| `loadGameHistoryFromFiles` | GET | Load all historical game files | Initial setup only |
| `updateTeamRecords` | POST | Calculate team win/loss records | Optional - done automatically |
| `updateWeeklyMetrics` | POST | Calculate SOS/PR/Ranking | Optional - done automatically |
| `setSOS` | POST | Calculate SOS only | Legacy/debugging |
| `calculatePowerRatings` | GET | Calculate PR only | Legacy/debugging |
| `calculateRankings` | GET | Calculate Ranking only | Legacy/debugging |

### **Predictions**
| Endpoint | Method | Purpose | Frequency |
|----------|--------|---------|-----------|
| `predictMatchup` | GET | Predict single game | As needed for next week |
| `predictMatchups` | POST | Predict multiple games | As needed for next week |

### **Utilities**
| Endpoint | Method | Purpose | Frequency |
|----------|--------|---------|-----------|
| `backfillAllMetrics` | POST | Recalculate all metrics for historical years | Rarely (after formula changes) |
| `calculateMatchupHistories` | POST | Calculate rivalry matchup histories | Once (or after rivalry seed changes) |
| `calculateScoreDeltas` | POST | Calculate average score differentials | Once (or when adding new data) |

---

## 🚨 Common Mistakes to Avoid

### ❌ DON'T:
1. **Skip `updateTeamRecords`** after loading games
   - TeamRecords table will be empty → metrics fail

2. **Run `updateWeeklyMetrics` for week 8 before running weeks 0-7**
   - Metrics are cumulative and week-dependent

3. **Use `RegularSeasonGames` instead of actual games played for predictions**
   - Fixed in current version (uses Wins + Losses)

4. **Forget to add new teams to Teams table**
   - Foreign key constraint will fail

5. **Load games with WinnerId/LoserId = -1**
   - Means team name lookup failed → add missing teams

### ✅ DO:
1. **Always run endpoints in sequence**:
   - Load games → Update records → Calculate metrics → Predict

2. **Run `updateWeeklyMetrics` for EVERY week from 0 to current**
   - Week 0 seeds the system
   - Each week builds on previous weeks

3. **Check for foreign key errors** if `updateTeamRecords` fails
   - Query: `SELECT * FROM Game WHERE Year=2025 AND (WinnerId=-1 OR LoserId=-1)`

4. **Use team-specific PPG/PAG for predictions**
   - Current system calculates from actual games played ✅

---

## 🔍 Validation Queries (SQLiteStudio)

### Check if games are loaded:
```sql
SELECT Year, Week, COUNT(*) as GameCount 
FROM Game 
WHERE Year = 2025 
GROUP BY Year, Week 
ORDER BY Week;
```

### Check if team records exist:
```sql
SELECT COUNT(*) FROM TeamRecords WHERE Year = 2025;
```

### Check if metrics are calculated:
```sql
SELECT t.TeamName, tr.Wins, tr.Losses, tr.PowerRating, tr.Ranking
FROM TeamRecords tr
JOIN Teams t ON tr.TeamID = t.TeamID
WHERE tr.Year = 2025 
  AND tr.PowerRating IS NOT NULL
ORDER BY tr.Ranking DESC
LIMIT 25;
```

### Find missing teams (WinnerId/LoserId = -1):
```sql
SELECT DISTINCT WinnerName FROM Game WHERE Year=2025 AND WinnerId=-1
UNION
SELECT DISTINCT LoserName FROM Game WHERE Year=2025 AND LoserId=-1;
```

---

## 🎓 Key Concepts

### **Strength of Schedule (SOS)**
- **BaseSOS**: Direct opponent win percentage
- **SubSOS**: Opponents' opponents win percentage
- **CombinedSOS**: 40% BaseSOS + 60% SubSOS
- **Calculated**: Week 6+ only (needs sufficient games)

### **Power Rating (PR)**
- Measures team quality relative to average
- Based on scoring performance vs opponent strength
- Adjusted for:
  - FBS vs FCS matchups (FCS = 0.25 weight)
  - Rivalry variance (EPIC/NATIONAL/STATE/MEH tiers)
  - Logarithmic Z-score dampening (prevents extremes)
- **Calculated**: Week 1+ (updates each week)

### **Ranking**
- **Formula**: `Win% × CombinedSOS × (1 + PowerRating)`
- The **operational metric** used for weekly comparisons
- Combines record quality, schedule strength, and team performance
- **Calculated**: Week 1+ (updates each week)

### **Predictions**
- **Base score**: Team PPG + Opponent PAG / 2 (for each team)
- **Adjustments**:
  - Historical win-differential (from AvgScoreDeltas)
  - Power Rating difference (×10 scaling)
  - Home field advantage (±2.5 points)
  - Week multiplier (1.05 early, 1.0 mid, 0.95 late season)
- **Confidence**: Rivalry tier increases variance (EPIC 1.3×, NATIONAL 1.2×, etc.)

---

## 📁 File Locations

### **Raw Data**
- `SaturdayPulse/NCAA Raw Game Data/*.txt` - Game results by year

### **Configuration/Docs**
- `Configuration/RANKING_SYSTEM_README.md` - Ranking formula details
- `Configuration/MATCHUP_HISTORY_README.md` - Rivalry system docs
- `Configuration/PREDICTION_SYSTEM_README.md` - Prediction formula details
- `Configuration/PREDICTION_VALIDATION_2025.md` - Test cases
- `Configuration/WORKFLOW_ROADMAP.md` - This file

### **Database**
- `ncaa_data.db` - SQLite database (use SQLiteStudio to view)

---

## 🛠️ Troubleshooting

### **"TeamRecords table is empty"**
- Run `updateTeamRecords` after loading games
- Check if games were actually loaded: `SELECT COUNT(*) FROM Game WHERE Year=2025`

### **"Foreign key constraint failed"**
- Find missing teams: Query above for WinnerId/LoserId = -1
- Add teams to Teams table manually
- Update Game table to fix -1 IDs

### **"Predicted scores too low (single digits)"**
- Fixed in latest version - uses actual games played (Wins + Losses)
- Old bug: used RegularSeasonGames (12) even when only 3-4 games played

### **"CORS error in Swagger"**
- Use PowerShell/terminal instead: `Invoke-RestMethod -Uri "..." -Method POST`
- Or restart browser and clear cache

### **"Build failed - file locked"**
- Stop running app: `Stop-Process -Name "SaturdayPulse" -Force`
- Then rebuild

---

## 📞 Quick Commands Cheat Sheet

### Stop app:
```powershell
Stop-Process -Name "SaturdayPulse" -Force
```

### Start app:
```powershell
cd D:\Development\SaturdayPulse\SaturdayPulse
dotnet run
```

### Load and process a single week (ONE COMMAND!):
```powershell
$year = 2025; $week = 6
Invoke-RestMethod -Uri "http://localhost:5086/api/gamedata/updateWeekGamesFromFile?year=$year&week=$week" -Method POST
```

### Predict a game:
```powershell
Invoke-RestMethod -Uri "http://localhost:5086/api/gamedata/predictMatchup?year=2025&teamName=Texas&opponentName=Oklahoma&location=N&week=8" -Method GET | ConvertTo-Json
```

---

## 🎯 Summary

**The Golden Rule**: Just call `updateWeekGamesFromFile` for each week!

The endpoint now does everything automatically:
1. **Loads games** from the file
2. **Updates records** (Wins/Losses/PointsFor/PointsAgainst)
3. **Calculates metrics** (week-aware: SOS/PR/Ranking based on week number)
4. **Returns status** confirming everything completed

**Then predict games** for upcoming weeks using `predictMatchup`!

Good luck and Hook 'em! 🤘
