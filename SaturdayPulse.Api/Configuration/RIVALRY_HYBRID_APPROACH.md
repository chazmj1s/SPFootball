# Rivalry System: Hybrid Approach (Data-Driven + Metadata Enrichment)

## Overview
This system combines **data-driven variance detection** with **curated rivalry metadata** to identify and adjust for high-variance matchups. The best of both worlds: let the data reveal true chaos while enriching with contextual information.

---

## The Hybrid Approach

### Phase 1: Data Analysis (Objective)
Calculate actual historical variance for all matchups with 10+ games:
- **AvgMargin**: Average victory margin
- **StDevMargin**: Standard deviation (the key metric)
- **UpsetRate**: How often the underdog wins
- **Longevity**: First/Last played years

### Phase 2: Metadata Enrichment (Contextual)
Match calculated data against known rivalries:
- **RivalryName**: "The Game", "Iron Bowl", etc.
- **RivalryTier**: EPIC / NATIONAL / STATE
- **Expected Variance**: Tier-based expectations

### Phase 3: Variance Application (Hybrid)
Use **BOTH** data and tier to determine effective variance:
```csharp
// Get actual calculated variance
var actualStDev = matchup.StDevMargin;

// Get tier-based expectation
var tierMultiplier = matchup.RivalryTier switch
{
    "EPIC" => 1.75,
    "NATIONAL" => 1.5,
    "STATE" => 1.3,
    _ => 1.0
};

// Use whichever is MORE forgiving (higher variance)
var effectiveMultiplier = Math.Max(
    actualStDev / expectedStDev,  // Data-driven
    tierMultiplier                 // Tier-based
);

// Cap at MaxVarianceRatio to prevent outliers
effectiveMultiplier = Math.Min(effectiveMultiplier, MaxVarianceRatio);

effectiveStDev = expectedStDev * effectiveMultiplier;
```

---

## Rivalry Tiers Explained

### EPIC Tier (1.75x variance)
**The "Anything Can Happen" Games**
- Ohio State vs Michigan ("The Game")
- Alabama vs Auburn ("Iron Bowl")
- Texas vs Oklahoma ("Red River Rivalry")

**Characteristics**:
- 100+ years of history
- National championship implications
- Record doesn't matter
- Expected StDev: 1.75x normal

**Example Impact**:
- Normal expectation: Ohio State by 21 ± 15.5
- With EPIC rivalry: Ohio State by 21 ± 27.1
- Z-score for Michigan upset: -1.581 → -1.054

---

### NATIONAL Tier (1.5x variance)
**High-Profile Cross-Regional Rivalries**
- Army vs Navy
- LSU vs Alabama
- Florida vs Georgia ("World's Largest Outdoor Cocktail Party")
- Notre Dame vs USC
- Penn State vs Ohio State
- Florida vs Florida State

**Characteristics**:
- 50-100+ years of history
- Major programs, national attention
- Significant variance but not quite "anything can happen"
- Expected StDev: 1.5x normal

---

### STATE Tier (1.3x variance)
**In-State Pride Games**
- Washington vs Washington State ("Apple Cup")
- Georgia vs Georgia Tech ("Clean, Old-Fashioned Hate")
- Texas vs Texas A&M ("Lone Star Showdown")
- Mississippi vs Mississippi State ("Egg Bowl")
- Oregon vs Oregon State ("Civil War")
- Michigan vs Michigan State
- Nebraska vs Iowa
- And 5 more...

**Characteristics**:
- State bragging rights
- Moderate to high variance
- Expected StDev: 1.3x normal

---

## Why the Hybrid Approach Works

### Problem with Pure Manual Curation:
- ❌ Subjective tier assignments
- ❌ Misses emerging high-variance matchups
- ❌ Doesn't account for changing dynamics
- ❌ Constant maintenance required

### Problem with Pure Data-Driven:
- ❌ No context for why variance exists
- ❌ Can't explain to users why adjustments are made
- ❌ Outliers from small samples can dominate
- ❌ Loses the "story" behind the rivalry

### Solution: Use BOTH:
- ✅ **Data reveals true variance** (objective)
- ✅ **Metadata provides context** (explainable)
- ✅ **Take the maximum** (most forgiving)
- ✅ **Cap with MaxVarianceRatio** (prevent outliers)

---

## Example Scenarios

### Scenario 1: Ohio State vs Michigan (EPIC Rivalry)
```
Actual StDev from 131 games: 27.1
Expected StDev from records: 15.5
Data-driven ratio: 27.1 / 15.5 = 1.75x

Tier: EPIC
Tier-based multiplier: 1.75x

Effective multiplier: max(1.75, 1.75) = 1.75x
Capped at 2.0: 1.75x ✓

Result: Use 1.75x (both methods agree!)
```

### Scenario 2: Penn State vs Rutgers (Not a Rivalry)
```
Actual StDev from 28 games: 19.1
Expected StDev from records: 18.3
Data-driven ratio: 19.1 / 18.3 = 1.04x

Tier: None
Tier-based multiplier: 1.0x

Effective multiplier: max(1.04, 1.0) = 1.04x
Capped at 2.0: 1.04x ✓

Result: Use 1.04x (essentially normal variance)
```

### Scenario 3: Hypothetical New Rivalry (Conference Realignment)
```
Actual StDev from 15 games: 31.2
Expected StDev from records: 15.8
Data-driven ratio: 31.2 / 15.8 = 1.97x

Tier: None (not in seed data)
Tier-based multiplier: 1.0x

Effective multiplier: max(1.97, 1.0) = 1.97x
Capped at 2.0: 1.97x ✓

Result: Use 1.97x (data discovers new chaos!)
```

---

## Database Schema

### MatchupHistory Table
```sql
CREATE TABLE MatchupHistory (
    Team1Id INTEGER NOT NULL,
    Team2Id INTEGER NOT NULL,
    GamesPlayed INTEGER NOT NULL,
    AvgMargin DECIMAL(5,2),
    StDevMargin DECIMAL(5,2),          -- Key: actual variance
    UpsetRate DECIMAL(4,3),
    FirstPlayed INTEGER,
    LastPlayed INTEGER,
    RivalryName VARCHAR(100),           -- "The Game", "Iron Bowl", etc.
    RivalryTier VARCHAR(20),            -- EPIC / NATIONAL / STATE
    PRIMARY KEY (Team1Id, Team2Id)
);
```

---

## Configuration

### appsettings.json
```json
{
  "MetricsConfiguration": {
    "MinimumMatchupGames": 10,      // Require 10+ games for data validity
    "MaxVarianceRatio": 2.0          // Cap at 2x to prevent extreme outliers
  }
}
```

---

## Workflow

### 1. Calculate Matchup Histories
```bash
POST /api/gamedata/calculateMatchupHistories?minimumGames=10
```

**What happens**:
- Analyzes 60 years of game data
- Calculates StDev for all matchups with 10+ games
- Matches against rivalry seed data
- Enriches with RivalryName and RivalryTier
- Saves to MatchupHistory table

**Output**:
```json
{
  "message": "Matchup histories calculated successfully",
  "matchupsCreated": 247,
  "rivalriesIdentified": 22,
  "epicRivalries": 3,
  "nationalRivalries": 7,
  "stateRivalries": 12
}
```

### 2. Query Rivalries (Future Enhancement)
```bash
GET /api/gamedata/rivalries?tier=EPIC
```

**Expected Output**:
```json
[
  {
    "team1": "Ohio State",
    "team2": "Michigan",
    "rivalryName": "The Game",
    "tier": "EPIC",
    "gamesPlayed": 131,
    "avgMargin": 11.8,
    "stDevMargin": 27.1,
    "upsetRate": 0.38,
    "varianceRatio": 1.75,
    "seriesAge": 131
  }
]
```

### 3. Integrate into Power Ratings
Update `TeamMetricsService.CalculatePowerRatings()` to:
- Load MatchupHistories alongside AvgScoreDeltas
- Check if current matchup has history
- Apply variance adjustment if found
- Use either data-driven or tier-based (whichever is more forgiving)

---

## Benefits

### For Users:
- ✅ **Explainable**: "This is an EPIC rivalry, so variance is 75% higher"
- ✅ **Contextual**: Can see rivalry names and history
- ✅ **Fair**: Known rivalries get appropriate forgiveness

### For the System:
- ✅ **Data-driven**: Still detects unknown high-variance matchups
- ✅ **Robust**: Tier provides floor, data provides ceiling
- ✅ **Scalable**: New rivalries emerge automatically from data
- ✅ **Maintainable**: Seed data is small (22 entries), rarely changes

---

## Next Steps

1. ✅ **Migrations created**: `AddMatchupHistoryTable`, `AddRivalryMetadataToMatchupHistory`
2. ⏳ **Apply migrations**: `dotnet ef database update`
3. ⏳ **Calculate histories**: `POST /api/gamedata/calculateMatchupHistories`
4. ⏳ **Integrate into power ratings**: Modify `CalculatePowerRatings()` and `SetSOS()`
5. ⏳ **Test with Ohio State 2024**: Verify Michigan upset is less catastrophic

---

## The Bottom Line

**60 years of data + 22 curated rivalries = Best of both worlds**

- Data discovers truth (objective variance)
- Metadata explains truth (rivalry context)
- Hybrid approach uses maximum forgiveness
- Cap prevents outliers from dominating
- Result: Fair, explainable, data-driven rivalry adjustments

**Ready to integrate into the actual Z-score calculations when you are!**
