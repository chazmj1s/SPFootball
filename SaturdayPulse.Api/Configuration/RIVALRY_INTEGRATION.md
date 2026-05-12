# Rivalry Variance Integration

## Overview
This document explains how MatchupHistory rivalry data is integrated into the power rating calculations to account for the unpredictability of rivalry games.

## The Problem
Traditional power ratings assume consistent performance variance based on win differentials. However, rivalry games often produce unexpected results regardless of team strength:
- **"The Game"** (Ohio State vs Michigan): Upsets happen even with large talent gaps
- **Iron Bowl** (Alabama vs Auburn): "Kick Six" defied all statistical predictions  
- **State rivalries**: In-state pride creates unpredictable outcomes

## The Solution
Apply **tier-based variance multipliers** to rivalry games when calculating Z-scores.

### Variance Multipliers by Tier
```csharp
"EPIC"     => 1.75x  // Century+ history, maximum chaos
"NATIONAL" => 1.5x   // Major cross-regional rivalries
"STATE"    => 1.3x   // In-state/regional rivalries
"MEH"      => 1.1x   // Lower-tier but still meaningful
```

### How It Works

#### Standard Z-Score Calculation
```csharp
zScore = (actualMargin - expectedMargin) / standardDeviation
```

#### With Rivalry Adjustment
```csharp
// Check if this matchup is a rivalry
var matchupHistory = matchupHistories.FirstOrDefault(m =>
    m.Team1Id == normalizedTeam1 && m.Team2Id == normalizedTeam2);

var effectiveStDev = standardDeviation;

if (matchupHistory != null)
{
    // Get tier multiplier (e.g., 1.75 for EPIC)
    var tierMultiplier = GetTierMultiplier(matchupHistory.RivalryTier);

    // Increase variance = lower Z-score magnitude
    effectiveStDev *= tierMultiplier;
}

zScore = (actualMargin - expectedMargin) / effectiveStDev;
```

### Impact on Ratings

**Example: Ohio State (11-0) vs Michigan (9-2) - "The Game"**

Without rivalry adjustment:
- Expected margin: +14 points (based on win differential)
- Actual margin: +3 points (upset alert!)
- Standard deviation: 12
- Z-score: (3 - 14) / 12 = **-0.92** (penalizes OSU heavily)

With rivalry adjustment (EPIC tier, 1.75x):
- Expected margin: +14 points
- Actual margin: +3 points  
- Effective std dev: 12 × 1.75 = **21**
- Z-score: (3 - 14) / 21 = **-0.52** (less penalty, recognizes rivalry chaos)

### Integration Points

#### 1. SetSOS (Strength of Schedule)
**File:** `Services/TeamMetricsService.cs`  
**Line:** ~320-380

- Loads MatchupHistory alongside AvgScoreDeltas
- Applies rivalry variance when calculating game weights
- Rivalry games get more forgiving Z-scores
- Affects BaseSOS, SubSOS, and CombinedSOS calculations

#### 2. CalculatePowerRatings
**File:** `Services/TeamMetricsService.cs`  
**Line:** ~540-620

- Loads MatchupHistory for the target year
- Applies same rivalry variance multiplier
- Power Rating = AvgZScore × CombinedSOS
- Rivalry adjustments ensure fair ratings even with unexpected results

## Benefits

### 1. More Accurate Ratings
- Teams aren't overly penalized for close rivalry losses
- Unexpected rivalry wins don't inflate ratings artificially

### 2. Better Predictions
- System recognizes "anything can happen" in rivalry games
- Confidence intervals widen appropriately for these matchups

### 3. Fair Assessment
- Alabama losing to Auburn in a tight game (even when heavily favored) doesn't tank their rating
- The system understands the context of the rivalry

## Data Source
All 50 rivalries are seeded from: `Data/RivalrySeedData.cs`
- Based on D1 FBS Top 50 Rivalries analysis
- Includes historical context (series age, game counts)
- Tier classifications based on historical variance and national significance

## Future Enhancements
1. **Hybrid Approach**: Use `Math.Max(actualVariance, tierVariance)` to let data override tiers when appropriate
2. **Dynamic Multipliers**: Adjust multipliers based on actual matchup history statistics
3. **Rivalry Detection**: Automatically identify new high-variance matchups as data accumulates
