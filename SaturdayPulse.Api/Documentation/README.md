# Metrics Configuration Guide

All hardcoded values for calculations have been moved to `appsettings.json` under the `MetricsConfiguration` section. This allows you to easily adjust parameters without modifying code.

## Configuration Location

Edit values in:
- **Production**: `appsettings.json`
- **Development**: `appsettings.Development.json` (overrides production values when running in Development mode)

## Available Parameters

### HomeFieldAdvantage
**Default**: `2.5`  
**Description**: Points added to expected margin when a team is playing at home. This matches the NCAA standard. Applied in Z-score calculations for both power ratings and strength of schedule.

**Example**: If Texas (13 wins) is expected to beat Vanderbilt (7 wins) by 20.4 points and the game is at Texas, the adjusted expectation becomes 20.4 + 2.5 = **22.9 points**.

---

### StandardSeasonGames
**Default**: `12`  
**Description**: Standard number of regular season games used for normalization. Post-season/bowl games are counted separately with reduced weight.

---

### ExtraWinBump
**Default**: `0.25`  
**Description**: Multiplier for post-season wins when calculating projected wins. Post-season wins count at 25% of regular season value.

**Example**: A team with 10 regular season wins and 2 bowl wins is normalized to: 10 + (2 × 0.25) = **10.5 wins**.

---

### ProjectedWinsHistoryYears
**Default**: `10`  
**Description**: Number of years of historical data to use when calculating projected wins for a team at the start of the season.

---

### SosWeekThreshold
**Default**: `6`  
**Description**: Week number when SOS calculation switches from projected wins to actual wins.
- **Before Week 6**: Uses 10-year historical projections
- **Week 6 and later**: Uses current season actual win totals

---

### ProjectedWinsRoundingThreshold
**Default**: `0.75`  
**Description**: Decimal threshold for rounding projected wins.
- **>= 0.75**: Round up
- **< 0.75**: Round down

**Example**:
- 7.8 wins → rounds to **8** (0.8 >= 0.75)
- 7.6 wins → rounds to **7** (0.6 < 0.75)

---

### DominantPerformanceThreshold
**Default**: `0.5`  
**Description**: Minimum Z-score to classify a game performance as "Dominant" in analysis outputs.

---

### UnderperformedThreshold
**Default**: `-0.5`  
**Description**: Maximum Z-score to classify a game performance as "Underperformed" in analysis outputs. Values between this and `DominantPerformanceThreshold` are "Expected".

**Performance Classification**:
- Z-score > 0.5 → **Dominant**
- -0.5 ≤ Z-score ≤ 0.5 → **Expected**
- Z-score < -0.5 → **Underperformed**

---

## Example Configuration

```json
{
  "MetricsConfiguration": {
    "HomeFieldAdvantage": 2.5,
    "StandardSeasonGames": 12,
    "ExtraWinBump": 0.25,
    "ProjectedWinsHistoryYears": 10,
    "SosWeekThreshold": 6,
    "ProjectedWinsRoundingThreshold": 0.75,
    "DominantPerformanceThreshold": 0.5,
    "UnderperformedThreshold": -0.5
  }
}
```

## Testing Different Values

To test with different parameters:

1. **For quick tests**: Edit `appsettings.Development.json`
2. **For production**: Edit `appsettings.json`
3. **Restart the application** for changes to take effect
4. **Run backfill endpoints** to recalculate all metrics with new values:
   - `POST /api/gamedata/backfillAllMetrics?year=2024`

## Where These Values Are Used

| Parameter | Used In | Impact |
|-----------|---------|--------|
| HomeFieldAdvantage | TeamMetricsService, GameDataController | Power ratings, SOS, game analysis |
| StandardSeasonGames | TeamMetricsService (projected wins) | Normalization of historical data |
| ExtraWinBump | TeamMetricsService (projected wins) | Post-season win weighting |
| ProjectedWinsHistoryYears | TeamMetricsService (projected wins) | Amount of historical data used |
| SosWeekThreshold | TeamMetricsService (SetSOS) | When to switch from projected to actual |
| ProjectedWinsRoundingThreshold | TeamMetricsService (projected wins) | Win projection rounding logic |
| DominantPerformanceThreshold | GameDataController (analyzeTeamGames) | Performance classification |
| UnderperformedThreshold | GameDataController (analyzeTeamGames) | Performance classification |

## Notes

- **No code changes required**: Simply edit JSON and restart
- **Version control friendly**: Different environments can use different values
- **Easy experimentation**: Test various home field advantages (2.0, 2.5, 3.0) without touching code
- **Consistent application**: All calculation points use the same values from one source
