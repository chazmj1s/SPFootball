# Cross-Divisional Penalty System

## Overview
Adjusts power ratings when FBS teams play FCS teams to prevent rating inflation/deflation from mismatched competition.

## The Problem
Without cross-divisional adjustment:
- **FBS teams beating FCS teams** get undeserved rating boosts
- **FCS teams dominating the top 25** due to small sample sizes (1-2 games)
- Example: North Dakota State (0-1) ranked #1 with PowerRating of 1.7663

## The Solution
Apply **variance multipliers** based on division matchup and game outcome.

### FBS Team Playing FCS Team

#### FBS Wins (Expected Result)
```csharp
effectiveStDev *= 2.0;  // Reduces positive Z-score by 50%
```
- **Impact**: Beating an FCS team gives minimal rating boost
- **Example**: Alabama beats Mercer 52-7 → small positive Z-score

#### FBS Loses (Upset!)
```csharp
effectiveStDev *= 0.5;  // Doubles negative Z-score
```
- **Impact**: Losing to FCS team is devastating to rating
- **Example**: Michigan loses to Appalachian State → massive penalty

### FCS Team Playing FBS Team

#### FCS Wins (Major Upset)
```csharp
effectiveStDev *= 0.5;  // Doubles positive Z-score
```
- **Impact**: Beating FBS team gives huge rating boost
- **Example**: James Madison beats Virginia Tech → major credit

#### FCS Loses (Expected Result)
```csharp
effectiveStDev *= 2.0;  // Reduces negative Z-score by 50%
```
- **Impact**: Losing to FBS team barely hurts rating
- **Example**: Mercer loses to Alabama → minimal penalty

## Implementation

### Step 1: Include Team Divisions in Query
```csharp
var gamesFromWinner = from g in context.Game
    where g.Year == targetYear
    join t in context.Team on g.WinnerId equals t.TeamID
    join opp in context.Team on g.LoserId equals opp.TeamID
    select new {
        TeamDivision = t.Division,
        OpponentDivision = opp.Division,
        // ... other fields
    };
```

### Step 2: Apply Penalty During Z-Score Calculation
```csharp
var isCrossDivisional = (teamDivision == "FBS" && oppDivision == "FCS") ||
                       (teamDivision == "FCS" && oppDivision == "FBS");

if (isCrossDivisional)
{
    if (teamDivision == "FBS")
    {
        if (delta > 0) // FBS won
            effectiveStDev *= 2.0; // Less credit
        else // FBS lost
            effectiveStDev *= 0.5; // More penalty
    }
    else // FCS team
    {
        if (delta > 0) // FCS won
            effectiveStDev *= 0.5; // More credit
        else // FCS lost
            effectiveStDev *= 2.0; // Less penalty
    }
}
```

## Integration Points

### 1. SetSOS (Strength of Schedule)
**File**: `Services/TeamMetricsService.cs` (~line 250-420)
- Applies cross-divisional penalty when calculating game weights
- Affects BaseSOS, SubSOS, and CombinedSOS

### 2. CalculatePowerRatings
**File**: `Services/TeamMetricsService.cs` (~line 550-695)
- Applies same penalty logic
- Ensures PowerRating = AvgZScore × CombinedSOS reflects true competition level

## Expected Results

### Before Cross-Divisional Penalty
```
Rank | Team                    | Record | PowerRating
-----------------------------------------------------
1    | North Dakota State      | 0-1    | 1.7663
2    | Central Arkansas        | 0-1    | 1.6249
16   | Ole Miss                | 10-3   | 0.6497
20   | Indiana                 | 11-2   | 0.5573
```

### After Cross-Divisional Penalty
```
Rank | Team                    | Record | PowerRating
-----------------------------------------------------
1    | Oregon                  | 13-0   | 1.2xxx
2    | Georgia                 | 11-2   | 1.1xxx
3    | Ohio State              | 14-2   | 1.0xxx
...
16   | Ole Miss                | 10-3   | 0.6497
20   | Indiana                 | 11-2   | 0.5573
```

## Stacking with Rivalry Adjustments

Cross-divisional and rivalry adjustments can **stack**:

```csharp
// Step 1: Check rivalry
if (matchupHistory != null)
    effectiveStDev *= rivalryTierMultiplier; // 1.1x to 1.75x

// Step 2: Check cross-divisional
if (isCrossDivisional)
    effectiveStDev *= crossDivisionalMultiplier; // 0.5x or 2.0x

// Step 3: Calculate Z-score
zScore = (delta - expected) / effectiveStDev;
```

**Example**: FBS team barely beats FCS rival
- Rivalry: 1.3x multiplier (STATE tier)
- Cross-divisional win: 2.0x multiplier
- Combined: effectiveStDev *= 2.6
- Result: Very small positive Z-score

## Benefits

1. **Accurate FBS Rankings**: FCS games no longer distort the top 25
2. **Fair FCS Rankings**: FCS teams evaluated against appropriate competition
3. **Upset Recognition**: Major upsets (App State over Michigan) properly weighted
4. **Scheduling Incentives**: FBS teams can't game the system with weak FCS opponents

## Configuration

Multipliers are hard-coded but could be moved to `MetricsConfiguration`:
```json
{
  "CrossDivisionalPenalty": {
    "FBS_WinMultiplier": 2.0,
    "FBS_LossMultiplier": 0.5,
    "FCS_WinMultiplier": 0.5,
    "FCS_LossMultiplier": 2.0
  }
}
```
