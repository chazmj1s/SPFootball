# Win Percentage Bucketing Proposal

## Current Problem
Raw win counts create sparse, outlier-prone buckets at season edges:
- 15-13 matchup had huge delta outlier
- Different season lengths (10, 11, 12 games) create artificial separation
- Post-season games inflate raw wins but ExtraWinBump (0.25) only applies to projections

## Proposed Solution: Win Percentage Buckets (2 decimal places)

### Bucket Definition
```
WinPct = Wins / (Wins + Losses)
Bucket = ROUND(WinPct, 2)
```

### Example Bucketing Table

| Current Raw Wins | Record Examples | Win % | Bucket | Notes |
|------------------|-----------------|-------|--------|-------|
| 0 wins | 0-12, 0-11, 0-10 | 0.00 | 0.00 | All winless teams |
| 1 win | 1-11, 1-10 | 0.08-0.09 | 0.08 or 0.09 | Very poor teams |
| 2 wins | 2-10, 2-11 | 0.15-0.17 | 0.15-0.17 | Poor teams |
| 3 wins | 3-9, 3-10 | 0.23-0.25 | 0.23-0.25 | Below average |
| 4 wins | 4-8, 4-9 | 0.31-0.33 | 0.31-0.33 | Below average |
| 5 wins | 5-7, 5-8 | 0.38-0.42 | 0.38-0.42 | Average |
| 6 wins | 6-6, 6-7, 6-8 | 0.43-0.50 | 0.43-0.50 | Average/Bowl eligible |
| 7 wins | 7-5, 7-6, 7-7 | 0.50-0.58 | 0.50-0.58 | Good |
| 8 wins | 8-4, 8-5, 8-6 | 0.57-0.67 | 0.57-0.67 | Good/Conference contender |
| 9 wins | 9-3, 9-4, 9-5 | 0.64-0.75 | 0.64-0.75 | Very good |
| 10 wins | 10-2, 10-3, 10-4 | 0.71-0.83 | 0.71-0.83 | Elite |
| 11 wins | 11-1, 11-2, 11-3 | 0.79-0.92 | 0.79-0.92 | Elite/Conference champ |
| 12 wins | 12-0, 12-1, 12-2 | 0.86-1.00 | 0.86-1.00 | National contender |
| 13 wins | 13-0, 13-1, 13-2 | 0.87-1.00 | 0.87-1.00 | Playoff team |
| 14 wins | 14-0, 14-1 | 0.93-1.00 | 0.93-1.00 | Playoff winner |
| 15 wins | 15-0, 15-1 | 0.94-1.00 | 0.94-1.00 | National champion |

### Matchup Example: What Changes

#### Current System (Raw Wins)
```
Ohio State (13-2) vs Michigan (8-5)
Lookup: Team1Wins=13, Team2Wins=8
Bucket: Sparse, may not exist or have tiny sample
```

#### Win Percentage System
```
Ohio State: 13/(13+2) = 0.87 → bucket 0.87
Michigan: 8/(8+5) = 0.62 → bucket 0.62
Lookup: Team1WinPct=0.87, Team2WinPct=0.62
Bucket: Much denser, combines all ~0.87 vs ~0.62 matchups
```

### Real-World Examples

| Matchup | Raw Wins | Win % | Bucketed | Sample Size Impact |
|---------|----------|-------|----------|-------------------|
| 12-0 vs 11-1 | 12 vs 11 | 1.00 vs 0.92 | 1.00 vs 0.92 | Combines with 13-0, 14-0, 15-0 |
| 10-2 vs 9-3 | 10 vs 9 | 0.83 vs 0.75 | 0.83 vs 0.75 | Combines with 11-2, 12-2 |
| 8-4 vs 7-5 | 8 vs 7 | 0.67 vs 0.58 | 0.67 vs 0.58 | Dense bucket, many games |
| 15-0 vs 13-2 | 15 vs 13 | 1.00 vs 0.87 | 1.00 vs 0.87 | **FIXES THE OUTLIER PROBLEM** |

### Benefits

1. **Normalizes season length**: 10-2 (83.3%), 11-2 (84.6%), 12-2 (85.7%) all bucket near each other
2. **Larger sample sizes**: Multiple raw-win combinations map to same win% bucket
3. **Natural post-season handling**: 12-2 and 13-2 are close (85.7% vs 86.7%) without needing ExtraWinBump
4. **Eliminates edge outliers**: No more 15-13 buckets with 2 games and a 40-point delta
5. **Intuitive**: "90% win team vs 60% win team" is easier to reason about

### Potential Concerns

1. **Loss of granularity**: 0.92 bucket combines 11-1 and 12-1 teams
   - **Counter**: Current system has even worse granularity (many missing buckets)

2. **Implementation complexity**: Need to change table schema and calculator
   - **Counter**: One-time change, cleaner logic going forward

3. **Historical recalculation**: All AvgScoreDeltas need rebuild
   - **Counter**: We already have the rebuild pipeline

### Recommended Bucket Precision

**Two decimal places (0.01 = 1% increments)**
- 0.00 to 1.00 = 101 possible buckets per team
- 101 × 101 = 10,201 possible matchup combinations
- Current system: 16 × 16 = 256 combinations (but many empty)
- Sweet spot: Dense enough for good samples, precise enough for quality predictions

### Alternative: One Decimal Place
- 0.0 to 1.0 = 11 possible buckets per team
- 11 × 11 = 121 possible matchup combinations
- **Too coarse**: Loses too much signal

### Implementation Sketch

```csharp
// New table schema
public class AvgScoreDelta
{
    [Column("Team1WinPct", TypeName = "decimal(3,2)")]
    public decimal Team1WinPct { get; set; }  // 0.00 to 1.00

    [Column("Team2WinPct", TypeName = "decimal(3,2)")]
    public decimal Team2WinPct { get; set; }  // 0.00 to 1.00

    // ... rest same
}

// Calculation
var team1WinPct = Math.Round((decimal)team1Record.Wins / (team1Record.Wins + team1Record.Losses), 2);
var team2WinPct = Math.Round((decimal)team2Record.Wins / (team2Record.Wins + team2Record.Losses), 2);
```

## Decision Point

Should we:
1. **Keep raw wins** (current system, known outliers)
2. **Switch to win percentage** (more robust, one-time migration)
3. **Hybrid approach** (use win% but keep ExtraWinBump logic somehow)

**Recommendation**: **Switch to win percentage**  
The season-length normalization alone justifies the change, and eliminating the 15-13 outlier case is a significant quality improvement.
