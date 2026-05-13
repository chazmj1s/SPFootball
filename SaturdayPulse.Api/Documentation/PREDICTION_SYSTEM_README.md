# Game Prediction System

## Overview
The prediction service generates realistic score projections for upcoming games by combining team-specific performance metrics, historical matchup data, and contextual factors like home field advantage and rivalry dynamics.

## Prediction Formula

### Base Score Calculation (Team-Specific)
```
Team Base Score = (Team PPG + Opponent PAG) / 2
Opponent Base Score = (Opponent PPG + Team PAG) / 2
```

Where:
- **PPG** = Points Per Game (PointsFor / RegularSeasonGames)
- **PAG** = Points Allowed Per Game (PointsAgainst / RegularSeasonGames)

This approach:
- ✅ Captures defensive-minded teams (low PPG)
- ✅ Accounts for offensive powerhouses (high PPG)
- ✅ Models matchup dynamics (good offense vs weak defense)

### Margin Adjustments
```
Predicted Team Score = Team Base + (Expected Delta / 2) × Week Multiplier
Predicted Opp Score = Opp Base - (Expected Delta / 2) × Week Multiplier
```

**Expected Delta Sources:**
1. **Historical Win Differential**: AvgScoreDelta from similar record matchups
2. **Power Rating Difference**: (Team PR - Opp PR) × 10 (conservative scaling)
3. **Home Field Advantage**: ±2.5 points for location

### Week-Based Scoring Multiplier
```
Week 1-4:   1.05 (offenses ahead of defenses early)
Week 5-10:  1.00 (normal mid-season scoring)
Week 11+:   0.95 (defenses optimized, weather factors)
```

### Confidence Intervals

**Standard Deviation**: From historical matchup variance (StDevP)

**Rivalry Multipliers** (increases uncertainty):
- EPIC: 1.3× (e.g., Ohio State-Michigan, Texas-Oklahoma)
- NATIONAL: 1.2× (e.g., Army-Navy, LSU-Alabama)
- STATE: 1.1× (regional rivalries)
- MEH: 1.0× (no adjustment)

**Margin of Error**: Capped at ±21 points (3 touchdowns) for practical predictions

## API Endpoints

### Single Matchup Prediction
```
POST /api/gamedata/predictMatchup
?year=2025
&team1Name=Texas
&team2Name=Oklahoma
&location=N  (H=home, A=away, N=neutral)
&week=8
```

### Multiple Matchup Predictions
```
POST /api/gamedata/predictMatchups
Body: [
  { "year": 2025, "team1Name": "Texas", "team2Name": "Oklahoma", "location": "N", "week": 8 },
  { "year": 2025, "team1Name": "Ohio State", "team2Name": "Michigan", "location": "A", "week": 15 }
]
```

## Performance Characteristics

Based on 2025 validation games:

| Metric | Performance |
|--------|-------------|
| **Spread Accuracy** | 2-1 (within 3-5 points on hits) |
| **Winner Prediction** | 100% (3/3 correct) |
| **Over/Under** | Improved with team-specific scoring |

**Key Improvements from Team PPG/PAG:**
- Accounts for defensive battles (low-PPG teams)
- Adjusts for offensive mismatches
- Reflects late-season defensive improvements

## Data Dependencies

**Required Fields:**
- `TeamRecords.PointsFor` / `PointsAgainst` (for PPG/PAG)
- `TeamRecords.PowerRating` (for quality adjustment)
- `AvgScoreDeltas` (for historical win-differential margins)
- `MatchupHistories` (for rivalry variance)
- `Teams` (for team lookup)

**Optional:**
- Historical game data (last 5 years) for baseline average fallback

## Example Predictions

### High-Scoring Matchup (Two Offensive Teams)
```
Team A: 35 PPG, 28 PAG (offensive powerhouse)
Team B: 32 PPG, 30 PAG (also high-scoring)
Expected Total: ~67 points
```

### Defensive Battle
```
Team A: 22 PPG, 18 PAG (defense-first)
Team B: 20 PPG, 16 PAG (also defensive)
Expected Total: ~38-42 points
```

### Late-Season Rivalry
```
Week 15 EPIC rivalry
Both teams defensive-minded
Week multiplier: 0.95
Rivalry variance: 1.3×
Result: Lower total, higher uncertainty
```

## Future Enhancements

Potential improvements:
- **Tempo/Pace metrics**: Fast-tempo teams = more possessions
- **Recent form**: Last 3-5 games trend
- **Weather impact**: Cold/rain for late-season games
- **Injury adjustments**: Key player availability
- **Conference strength**: Adjust for SOS within predictions

## Usage Workflow

1. **Load season data** through Week N
2. **Calculate weekly metrics** (SOS, PowerRating, Ranking)
3. **Calculate matchup histories** if not already done
4. **Call prediction endpoint** for Week N+1 games
5. **Review predictions** alongside confidence intervals
6. **Compare to actual results** after games complete

The system is designed for weekly operational use, predicting next week's games based on current-season performance through the most recent completed week.
