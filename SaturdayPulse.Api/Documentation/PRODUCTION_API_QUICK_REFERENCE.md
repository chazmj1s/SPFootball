# Production API Quick Reference

## Base URL
```
http://localhost:5086/api/productiongamedata
```

## Endpoints

### 1. Predict Single Matchup
**GET** `/predictMatchup`

**Parameters:**
- `year` (optional, default: current year)
- `teamName` (required)
- `opponentName` (required)
- `location` (optional, default: 'N')
  - 'H' = team is home
  - 'A' = team is away
  - 'N' = neutral site
- `week` (optional, default: 0)

**Example:**
```bash
curl "http://localhost:5086/api/productiongamedata/predictMatchup?year=2025&teamName=Ohio State&opponentName=Michigan&location=H&week=12"
```

**Response:**
```json
{
  "matchup": "Ohio State vs Michigan",
  "prediction": "Ohio State 27.5, Michigan 24.3",
  "expectedMargin": 3.2,
  "marginOfError": 12.5,
  "confidence": "Medium",
  "teamRecord": "11-?",
  "opponentRecord": "10-?",
  "teamPowerRating": 0.0234,
  "opponentPowerRating": 0.0189,
  "rivalryNote": "EPIC rivalry detected",
  "summary": "Close matchup with slight home field advantage"
}
```

---

### 2. Predict Multiple Matchups
**POST** `/predictMatchups`

**Body:**
```json
{
  "year": 2025,
  "matchups": [
    {
      "teamName": "Ohio State",
      "opponentName": "Michigan",
      "location": "H",
      "week": 12
    },
    {
      "teamName": "Alabama",
      "opponentName": "Auburn",
      "location": "N",
      "week": 13
    }
  ]
}
```

**Example:**
```bash
curl -X POST "http://localhost:5086/api/productiongamedata/predictMatchups" \
  -H "Content-Type: application/json" \
  -d '{"year":2025,"matchups":[{"teamName":"Ohio State","opponentName":"Michigan","location":"H","week":12}]}'
```

**Response:**
```json
{
  "message": "Predicted 2 matchups for 2025",
  "predictions": [
    {
      "matchup": "Ohio State vs Michigan",
      "prediction": "Ohio State 27.5, Michigan 24.3",
      "expectedMargin": 3.2,
      "marginOfError": 12.5,
      "confidence": "Medium",
      "rivalryNote": "EPIC rivalry detected",
      "summary": "Close matchup with slight home field advantage"
    },
    ...
  ]
}
```

---

### 3. Query Team Records
**GET** `/queryTeamRecords`

**Parameters:**
- `wins` (optional) - Exact wins
- `losses` (optional) - Exact losses
- `minWins` (optional) - Minimum wins
- `maxWins` (optional) - Maximum wins
- `startYear` (optional) - Start year filter
- `endYear` (optional) - End year filter
- `minPowerRating` (optional) - Minimum power rating
- `maxPowerRating` (optional) - Maximum power rating
- `limit` (optional, default: 50) - Max results

**Example:**
```bash
# Find all 13-0 teams
curl "http://localhost:5086/api/productiongamedata/queryTeamRecords?wins=13&losses=0"

# Find teams with 10+ wins in 2020-2024
curl "http://localhost:5086/api/productiongamedata/queryTeamRecords?startYear=2020&endYear=2024&minWins=10"

# Find teams with power rating between -0.02 and 0.01
curl "http://localhost:5086/api/productiongamedata/queryTeamRecords?minPowerRating=-0.02&maxPowerRating=0.01"
```

**Response:**
```json
{
  "count": 42,
  "filters": {
    "wins": null,
    "losses": null,
    "minWins": 10,
    "maxWins": null,
    "startYear": 2020,
    "endYear": 2024,
    "minPowerRating": null,
    "maxPowerRating": null,
    "limit": 50
  },
  "results": [
    {
      "year": 2024,
      "teamName": "Georgia",
      "record": "13-1",
      "wins": 13,
      "losses": 1,
      "pointsFor": 523,
      "pointsAgainst": 234,
      "pointDifferential": 289,
      "baseSOS": 0.523,
      "subSOS": 0.489,
      "combinedSOS": 0.5074,
      "powerRating": 0.0234
    },
    ...
  ]
}
```

---

### 4. Query Rivalries
**GET** `/rivalries`

**Parameters:**
- `tier` (optional) - Filter by rivalry tier
  - `EPIC` - Legendary rivalries (The Game, Iron Bowl, etc.)
  - `National` - Major national rivalries
  - `State` - Important state rivalries
  - `MEH` - Notable matchups
  - `ALL` or omit - Return all
- `minGames` (optional) - Minimum historical games played
- `minVarianceRatio` (optional) - Minimum variance ratio (volatility)

**Example:**
```bash
# Get all EPIC rivalries
curl "http://localhost:5086/api/productiongamedata/rivalries?tier=EPIC"

# Get rivalries with 50+ games
curl "http://localhost:5086/api/productiongamedata/rivalries?minGames=50"

# Get all rivalries
curl "http://localhost:5086/api/productiongamedata/rivalries"
```

**Response:**
```json
{
  "totalMatchups": 50,
  "totalInDatabase": 50,
  "filters": {
    "tier": "EPIC",
    "minGames": 0,
    "minVarianceRatio": 0.0
  },
  "rivalries": [
    {
      "team1": "Ohio State",
      "team2": "Michigan",
      "rivalryName": "The Game",
      "tier": "EPIC",
      "gamesPlayed": 119,
      "avgMargin": 11.2,
      "stDevMargin": 18.5,
      "upsetRate": 0.235,
      "varianceRatio": 1.23,
      "seriesAge": 119,
      "firstPlayed": 1897,
      "lastPlayed": 2024
    },
    ...
  ]
}
```

---

## Common Use Cases

### Predict This Week's Games
```bash
# Week 12 predictions for 2025
curl -X POST "http://localhost:5086/api/productiongamedata/predictMatchups" \
  -H "Content-Type: application/json" \
  -d '{
    "year": 2025,
    "matchups": [
      {"teamName":"Ohio State","opponentName":"Michigan","location":"H","week":12},
      {"teamName":"Alabama","opponentName":"Auburn","location":"N","week":12},
      {"teamName":"Texas","opponentName":"Texas A&M","location":"H","week":12}
    ]
  }'
```

### Find Dominant Teams
```bash
# Teams with 12+ wins and high power rating in 2024
curl "http://localhost:5086/api/productiongamedata/queryTeamRecords?year=2024&minWins=12&minPowerRating=0.02"
```

### Analyze Epic Rivalries
```bash
# Get all EPIC tier rivalries with 50+ historical games
curl "http://localhost:5086/api/productiongamedata/rivalries?tier=EPIC&minGames=50"
```

---

## Error Responses

### 400 Bad Request
```json
{
  "error": "Both teamName and opponentName are required"
}
```

### 404 Not Found
```json
{
  "error": "Team not found: InvalidTeamName"
}
```

### 500 Internal Server Error
```json
{
  "error": "An error occurred while predicting matchup."
}
```

---

## Notes
- All predictions use current week data through the specified week
- Power ratings are week-aware and recalculated weekly
- Rivalry detection is automatic for the 50 curated matchups
- Home field advantage is included in all predictions
- Confidence levels: High (>75%), Medium (50-75%), Low (<50%)
