# Matchup History & Rivalry Detection System

## Overview
This system automatically detects high-variance matchups (rivalries) by analyzing 60 years of historical game data. Instead of manually curating rivalry lists, the data reveals which matchups consistently defy win-based expectations.

---

## How It Works

### 1. **MatchupHistory Table**
Stores pre-calculated statistics for every team pairing with sufficient game history:

| Column | Type | Description |
|--------|------|-------------|
| `Team1Id` | INT | Lower team ID (for consistency) |
| `Team2Id` | INT | Higher team ID (for consistency) |
| `GamesPlayed` | INT | Total games between these teams |
| `AvgMargin` | DECIMAL | Average victory margin (absolute value) |
| `StDevMargin` | DECIMAL | Standard deviation of margins (rivalry indicator) |
| `UpsetRate` | DECIMAL | % of games won by team with fewer season wins |
| `FirstPlayed` | INT | First year they played |
| `LastPlayed` | INT | Most recent year they played |

---

### 2. **Rivalry Detection Logic**

**During Z-score calculation**:
```csharp
// Get expected variance from win-based AvgScoreDeltas
var expectedStDev = asd.StDevP;

// Check if this specific matchup has historical data
var matchup = await context.MatchupHistories
    .FirstOrDefaultAsync(m => 
        (m.Team1Id == Min(teamId, oppId) && m.Team2Id == Max(teamId, oppId)) &&
        m.GamesPlayed >= MinimumMatchupGames);

if (matchup != null)
{
    var actualStDev = (double)matchup.StDevMargin;
    var varianceRatio = actualStDev / expectedStDev;

    // Cap at MaxVarianceRatio to prevent outliers
    var cappedRatio = Math.Min(varianceRatio, MaxVarianceRatio);

    // Use the higher variance (more forgiving)
    effectiveStDev = expectedStDev * cappedRatio;
}
else
{
    // Not enough games, use general expectation
    effectiveStDev = expectedStDev;
}

zScore = (actualDelta - expectedDelta) / effectiveStDev;
```

**Key Insight**: Rivalries naturally have higher `StDevMargin` than expected from win records alone. Using this higher value reduces Z-score penalties for unexpected results.

---

### 3. **Configuration Parameters**

From `appsettings.json`:

```json
"MetricsConfiguration": {
  "MinimumMatchupGames": 10,    // Require 10+ games for statistical validity
  "MaxVarianceRatio": 2.0        // Cap at 2x to prevent extreme adjustments
}
```

**MinimumMatchupGames**:
- Too low (5): Noise from limited data
- Too high (20): Misses newer rivalries
- Sweet spot (10-15): Captures true long-term patterns with 60 years of data

**MaxVarianceRatio**:
- Prevents outlier matchups from getting 3x-4x multipliers
- 2.0 means a matchup can be at most twice as chaotic as expected

---

### 4. **Example Matchups (Hypothetical)**

| Matchup | Games | Expected StDev | Actual StDev | Variance Ratio | Effect |
|---------|-------|----------------|--------------|----------------|--------|
| **Ohio State vs Michigan** | 120 | 15.5 | 27.1 | **1.75x** | Major forgiveness for upsets |
| **Texas vs Oklahoma** | 118 | 15.5 | 22.8 | **1.47x** | Moderate forgiveness |
| **Alabama vs Auburn** | 88 | 16.2 | 29.4 | **1.81x** | High forgiveness (Iron Bowl chaos) |
| **Texas vs UTSA** | 3 | 14.6 | - | **1.0x** | Not enough games, use default |
| **Penn State vs Rutgers** | 28 | 18.3 | 19.1 | **1.04x** | Not a rivalry (low variance) |

**Interpretation**:
- Ohio State/Michigan gets 1.75x variance allowance → upsets hurt less
- Penn State/Rutgers shows normal variance → no special treatment
- UTSA doesn't qualify → uses general expectations

---

## Usage Workflow

### **Step 1: Calculate Matchup Histories**
After loading historical data, run:

```bash
POST /api/gamedata/calculateMatchupHistories?minimumGames=10
```

**What it does**:
- Analyzes all 60 years of game data
- Groups by team pairings
- Calculates StDev, upset rate, longevity
- Saves to `MatchupHistory` table

**Output Example**:
```json
{
  "message": "Matchup histories calculated successfully",
  "matchupsCreated": 247,
  "minimumGames": 10,
  "nextStep": "Matchup-specific variance will now be used in power rating calculations"
}
```

### **Step 2: Recalculate Power Ratings**
Run the standard backfill to apply new variance adjustments:

```bash
POST /api/gamedata/backfillAllMetrics?year=2024
```

Power ratings will now account for matchup-specific variance!

### **Step 3: Verify Impact**
Check specific games:

```bash
GET /api/gamedata/analyzeTeamGames?teamId=85&year=2024
```

**Before (without matchup variance)**:
- Ohio State vs Michigan (Home): Z-score = **-1.581**

**After (with matchup variance)**:
- Ohio State vs Michigan (Home): Z-score = **-1.054** (33% less penalty)

---

## Advantages of This Approach

✅ **Data-Driven**: No subjective "is this a rivalry?" decisions  
✅ **Self-Updating**: Variance ratios adjust as new games are added  
✅ **Longevity Filter**: 10+ game minimum ensures true patterns, not noise  
✅ **Discovers Unknowns**: Might reveal unexpected high-variance matchups  
✅ **Statistically Sound**: Uses actual historical variance vs expected  
✅ **No Manual Curation**: Works automatically with 60 years of data  

---

## Configuration Tuning

### If you want **more matchups** to qualify:
```json
"MinimumMatchupGames": 8
```
- Includes newer rivalries (e.g., recent conference realignment)
- Risk: Less statistical confidence

### If you want **more conservative forgiveness**:
```json
"MaxVarianceRatio": 1.5
```
- Limits maximum adjustment to 50% above expected
- Prevents extreme outliers from dominating

### If you want **stricter qualification**:
```json
"MinimumMatchupGames": 15
```
- Only deep, longstanding rivalries qualify
- Misses newer but intense matchups

---

## Future Enhancements

1. **Query Endpoint**: `GET /api/gamedata/rivalries?minGames=15&minVarianceRatio=1.3`
   - List all detected high-variance matchups
   - Sort by longevity, upset rate, variance ratio

2. **Rivalry Metadata**: Add manual `RivalryName` field for display
   - "The Game", "Iron Bowl", "Red River Rivalry"
   - Still use calculated variance, just add friendly names

3. **Conference Championship Rematches**: Detect and boost variance for playoff/championship games between teams that played earlier in season

---

## Migration

Created migration: `AddMatchupHistoryTable`

Apply with:
```bash
cd SaturdayPulse
dotnet ef database update --context NCAAContext
```

---

## Key Takeaway

**Let the data speak**: 60 years of history reveals which matchups consistently defy expectations. Longevity + high variance = true rivalry, automatically detected without manual lists or subjective tiers.
