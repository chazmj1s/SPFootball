# Ranking System

## Overview

The Ranking system combines three key factors to create a comprehensive team rating that can be calculated weekly during the season:

```
Ranking = WinPercentage × CombinedSOS × (1 + PowerRating)
```

## Components

### 1. Win Percentage (Base Metric)
- **Formula**: `Wins / (Wins + Losses)`
- **Purpose**: Rewards teams for winning games (fundamental requirement)
- **Range**: 0.0 (0 wins) to 1.0 (undefeated)

### 2. CombinedSOS (Schedule Strength Multiplier)
- **Formula**: `(2 × BaseSOS + 3 × SubSOS) / 5`
  - **BaseSOS**: Direct opponent quality (40% weight)
  - **SubSOS**: Opponents' opponents quality (60% weight)
- **Purpose**: Rewards teams for playing tough schedules
- **Typical Range**: 0.85 to 1.15
- **Weighting Rationale**: 60% SubSOS emphasizes sustained schedule difficulty over isolated marquee games

### 3. PowerRating (Performance Quality Adjustment)
- **Formula**: `AvgZScore × CombinedSOS`
  - **AvgZScore**: Logarithmically dampened Z-scores measuring performance vs expectations
  - Incorporates rivalry variance multipliers
  - Applies FBS/FCS division weighting (0.25 for FCS, 1.0 for FBS)
- **Purpose**: Adjusts for quality of performance (upsets, dominant wins, bad losses)
- **Typical Range**: -0.5 to +0.5
- **Multiplier Effect**: Added to 1.0, so:
  - PR of +0.3 → 1.3x multiplier (30% bonus)
  - PR of -0.2 → 0.8x multiplier (20% penalty)
  - PR of 0 → 1.0x multiplier (neutral)

## Philosophy

### The Three-Factor Balance

This system balances three philosophies:

1. **Winning matters** (Win %)
   - You must win games to rank highly
   - A 5-7 team cannot outrank a 13-3 team on win% alone

2. **Competition matters** (SOS)
   - Playing tough opponents increases your rating potential
   - Beating cupcakes provides limited upside

3. **Quality matters** (PowerRating)
   - HOW you win/lose affects your final rating
   - Upsets and dominant performances are rewarded
   - Bad losses to weak opponents are penalized

### Why This Works

**Before Ranking:**
- Kansas (5-7, PR 0.565) ranked #1 due to massive upset wins
- Ohio State (14-2, National Champion, PR 0.1461) ranked lower
- **Problem**: Ignored win-loss record entirely

**After Ranking:**
- Win% anchors the rating (5-7 = 0.417, 14-2 = 0.875)
- SOS and PR adjust from that baseline
- Kansas still gets credit for quality wins but can't overcome losing record

**2024 Example Results:**

| Rank | Team | W-L | Win% | SOS | PR | **Ranking** |
|------|------|-----|------|-----|-----|-------------|
| 1 | Indiana | 11-2 | .846 | 1.024 | 0.2723 | **1.1024** |
| 2 | Ohio State | 14-2 | .875 | 1.0306 | 0.1461 | **1.0335** |
| 3 | Ole Miss | 10-3 | .769 | 0.9846 | 0.3 | **0.9846** |
| 81 | Kansas | 5-7 | .417 | 1.0342 | 0.565 | **0.675** |

## Implementation

### Database Schema

```sql
ALTER TABLE TeamRecords 
ADD COLUMN Ranking DECIMAL(10,4) NULL;
```

### Calculation Order

1. **SetSOS** - Calculate BaseSOS, SubSOS, and CombinedSOS
2. **CalculatePowerRatings** - Calculate Z-scores with logarithmic dampening and rivalry adjustments
3. **CalculateRankings** - Combine Win%, SOS, and PR into final metric

This can be run weekly during the season as new game results are loaded.

### API Endpoints

#### Calculate Rankings (Single Year)
```
POST /api/gamedata/calculateRankings?year=2024
```

Can be run weekly after loading new game data.

#### Backfill All Metrics (Including Rankings)
```
POST /api/gamedata/backfillAllMetrics?startYear=2000
```

This will run SetSOS → CalculatePowerRatings → CalculateRankings for each year.

## FCS Teams

FCS teams receive `NULL` for:
- PowerRating (no valid comparison base)
- CombinedSOS (not calculated)
- Ranking (depends on PR and SOS)

FCS opponents count as 0.25 weight in FBS team calculations.

## Future Considerations

### Potential Adjustments

1. **Win% component weight**
   - Could adjust to `Win%^0.8` to reduce impact of record slightly
   - Would allow quality teams with tough schedules to rise more

2. **SOS/PR balance**
   - Current formula treats them equally (both multiply Win%)
   - Could weight one more heavily: `Win% × (SOS^0.6) × (1 + PR)^0.4`

3. **Rivalry bonus/penalty**
   - Currently embedded in PR via variance multipliers
   - Could add explicit rivalry win/loss bonuses

4. **Conference strength normalization**
   - Could adjust SOS by conference average to prevent conference bias

## Historical Context

The Ranking system was created after observing that PowerRating alone (quality-focused) produced unrealistic rankings where teams with losing records but quality wins ranked above national champions. By anchoring to Win% and using PR/SOS as multipliers rather than standalone metrics, the system maintains the quality-focused philosophy while respecting the fundamental importance of winning games.

The name "Ranking" (not "FinalRating") reflects that this can be calculated weekly during the season as new results come in, providing an up-to-date assessment of team quality.
