# 2025 Prediction Validation Tests

## Test Cases - Actual Results

### Game 1: Ohio State @ Michigan (Week 15)
**Actual Result: Ohio State 27, Michigan 9 (OSU by 18)**
- Location: @ Michigan
- Rankings: OSU #1, Michigan #23
- Rivalry: EPIC (The Game)

### Game 2: Texas A&M @ Texas (Week 15)
**Actual Result: Texas 27, Texas A&M 17 (Texas by 10)**
- Location: @ Texas (Austin)
- Rankings: Texas #7, A&M #16
- Rivalry: EPIC (Lone Star Showdown)

### Game 3: Texas vs Oklahoma (Week 8)
**Actual Result: Texas 23, Oklahoma 6 (Texas by 17)**
- Location: Neutral (Cotton Bowl, Dallas)
- Rankings: Texas #7, Oklahoma #13
- Rivalry: EPIC (Red River Shootout)

---

## New Prediction System Logic

### Key Changes from Original:
1. **Team-Specific Scoring** (instead of fixed league average)
   - Uses PointsFor/Game (PPG) and PointsAgainst/Game (PAG)
   - Team Base = (Team PPG + Opp PAG) / 2
   - Opp Base = (Opp PPG + Team PAG) / 2

2. **Week-Based Multiplier**
   - Weeks 1-4: ×1.05
   - Weeks 5-10: ×1.00
   - Weeks 11-15: ×0.95

3. **Conservative PR Scaling**
   - PR difference × 10 (0.1 PR = 1 point)

---

## Manual Validation Process

To validate the new system against these three games:

### Step 1: Load 2025 Season Data
```
GET http://localhost:5086/api/gamedata/loadGameHistoryFromFiles
POST http://localhost:5086/api/gamedata/updateTeamRecords?year=2025
```

### Step 2: Calculate Metrics Through Each Week
For weeks 1-14 (before the prediction):
```
POST http://localhost:5086/api/gamedata/updateWeeklyMetrics?year=2025&week=1
POST http://localhost:5086/api/gamedata/updateWeeklyMetrics?year=2025&week=2
...
POST http://localhost:5086/api/gamedata/updateWeeklyMetrics?year=2025&week=14
```

### Step 3: Test Predictions

**Test 1: Texas vs Oklahoma (Week 8)**
Prerequisites: Load weeks 1-7, calculate metrics through week 7
```
POST http://localhost:5086/api/gamedata/predictMatchup?year=2025&team1Name=Texas&team2Name=Oklahoma&location=N&week=8
```

Expected improvements:
- If Oklahoma had low PPG through week 7, base score should be lower
- Week 8: multiplier = 1.00 (mid-season)
- EPIC rivalry: 1.3× variance

**Test 2: Ohio State @ Michigan (Week 15)**
Prerequisites: Load weeks 1-14, calculate metrics through week 14
```
POST http://localhost:5086/api/gamedata/predictMatchup?year=2025&team1Name=Ohio%20State&team2Name=Michigan&location=A&week=15
```

Expected improvements:
- Both defensive teams → low PPG → lower base scores
- Week 15: multiplier = 0.95 (late season)
- EPIC rivalry: 1.3× variance
- Predicted total should be closer to 36 actual (vs old ~52)

**Test 3: Texas A&M @ Texas (Week 15)**
Prerequisites: Load weeks 1-14, calculate metrics through week 14
```
POST http://localhost:5086/api/gamedata/predictMatchup?year=2025&team1Name=Texas&team2Name=Texas%20A%26M&location=H&week=15
```

Expected improvements:
- Texas decent PPG, A&M lower PPG → asymmetric base scores
- Week 15: multiplier = 0.95
- Home field: Texas +2.5
- Predicted total should be closer to 44 actual (vs old ~55)

---

## Expected Performance Improvements

### Over/Under Predictions

| Game | Old Total | New Total (Est) | Actual | Improvement |
|------|-----------|-----------------|--------|-------------|
| OSU-Michigan | ~52 | ~38-42 | 36 | ✅ Much closer |
| Texas-A&M | ~55 | ~46-48 | 44 | ✅ Much closer |
| Texas-OU | ~60 | ~42-48* | 29 | ⚠️ Better but still high |

*Depends on Oklahoma's PPG through week 7

### Spread Predictions

Should remain accurate (2-3 for 3):
- OSU-Michigan: OSU by 13-16 → Actual 18 ✅
- Texas-A&M: Texas by 8-12 → Actual 10 ✅
- Texas-OU: Texas by 4-7 → Actual 17 ❌

---

## Quick Test Script (PowerShell)

Once the app is running on port 5086:

```powershell
# Load data
Invoke-WebRequest -Uri "http://localhost:5086/api/gamedata/loadGameHistoryFromFiles" -Method GET

# Update records
Invoke-WebRequest -Uri "http://localhost:5086/api/gamedata/updateTeamRecords?year=2025" -Method POST

# Calculate metrics through week 7 (for Texas-OU test)
for ($i=0; $i -le 7; $i++) {
    Invoke-WebRequest -Uri "http://localhost:5086/api/gamedata/updateWeeklyMetrics?year=2025&week=$i" -Method POST
    Write-Host "Week $i metrics calculated"
}

# Test prediction
$result = Invoke-WebRequest -Uri "http://localhost:5086/api/gamedata/predictMatchup?year=2025&team1Name=Texas&team2Name=Oklahoma&location=N&week=8" -Method POST
$result.Content | ConvertFrom-Json | ConvertTo-Json -Depth 10
```

---

## Notes

- The Texas-Oklahoma game (week 8) will be the most interesting test because it happens mid-season
- The two week-15 games should both benefit from the 0.95 late-season multiplier
- All three are EPIC rivalries, so the 1.3× variance multiplier applies
- The PPG/PAG approach should catch defensive teams much better than the fixed average

Once you've loaded the full 2025 season, run these tests and document the actual prediction outputs here for comparison!
