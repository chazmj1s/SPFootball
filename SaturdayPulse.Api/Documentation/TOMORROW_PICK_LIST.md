# Development Pick List - Next Session

## Priority 1: Fix ~10 Point Over-Prediction 🎯
**Status:** High Priority - Accuracy Issue  
**Estimated Time:** 1-2 hours  
**Impact:** Improves prediction quality from B+ to A-

### Issue:
- Predictions consistently over-predict totals by ~10 points
- Examples:
  - OSU vs Michigan: Predicted 43.0, Actual 36 (error: +7.0)
  - Texas vs A&M: Predicted 50.1, Actual 44 (error: +6.1)
  - Texas vs OU: Predicted 46.2, Actual 29 (error: +17.2)

### Root Causes:
1. **PPG/PAG baselines too high** for defensive battles
2. **Week multiplier insufficient** (late season 0.95 not enough)
3. **Playoff/rivalry intensity** not factored into defense
4. **Weather/conditions** not considered

### Proposed Solutions (Pick One or Combine):

#### Option A: Simple Multiplier (Easiest)
```csharp
// In GamePredictionService.cs, after calculating predictedTeamScore/predictedOppScore
predictedTeamScore *= 0.92;  // Reduce by 8%
predictedOppScore *= 0.92;
```
**Pros:** One-line fix, immediate 8% reduction  
**Cons:** Doesn't address root cause  
**Impact:** 43.0 → 39.5, 50.1 → 46.1, 46.2 → 42.5 (much better!)

#### Option B: Late-Season Defensive Adjustment (Better)
```csharp
// Increase late-season multiplier impact
double weekMultiplier = week switch
{
    <= 4 => 1.05,    // Early season: offenses ahead
    >= 11 => 0.88,   // Late season: defenses dominant (was 0.95)
    _ => 1.00
};
```
**Pros:** Targets the actual problem (late-season defense)  
**Cons:** Only helps weeks 11+  
**Impact:** Week 15 games would drop ~12%

#### Option C: Rivalry Defensive Intensity (Best)
```csharp
// Add defensive intensity factor for big games
double defensiveIntensity = 1.0;
if (rivalry != null)
{
    defensiveIntensity = rivalry.RivalryTier switch
    {
        "EPIC" => 0.90,      // 10% fewer points
        "NATIONAL" => 0.93,  // 7% fewer points
        "STATE" => 0.95,     // 5% fewer points
        _ => 1.0
    };
}
predictedTeamScore *= defensiveIntensity;
predictedOppScore *= defensiveIntensity;
```
**Pros:** Targets rivalry games specifically  
**Cons:** Doesn't help non-rivalry games  
**Impact:** EPIC rivalry totals drop 10%

#### Recommendation: **Combine A + C**
- Apply 0.92× base multiplier (fixes all games)
- Apply additional rivalry defensive intensity (fixes big games)
- Result: Most games -8%, rivalry games -17%

---

## Priority 2: Clean Up Controllers 🧹
**Status:** Medium Priority - Technical Debt  
**Estimated Time:** 2-3 hours  
**Impact:** Better maintainability, cleaner API surface

### Current State:
`GameDataController.cs` has **~50+ endpoints** mixing:
- Production prediction endpoints
- Development data-loading utilities
- Debugging/analysis tools
- Metric calculation triggers
- Manual override functions

### Proposed Reorganization:

#### Keep in `GameDataController.cs` (Production):
```
GET  /api/gamedata/predictMatchup           ← Production
POST /api/gamedata/predictMatchups          ← Production
GET  /api/gamedata/rankings                 ← Production
GET  /api/gamedata/teamRecord               ← Production
GET  /api/gamedata/teamMetrics              ← Production
```

#### Move to `GameDataManagementController.cs` (Admin):
```
POST /api/gamedatamanagement/updateWeekGamesFromFile
POST /api/gamedatamanagement/updateTeamRecords
POST /api/gamedatamanagement/updateWeeklyMetrics
POST /api/gamedatamanagement/backfillAllMetrics
POST /api/gamedatamanagement/recalculateScoreDeltas
POST /api/gamedatamanagement/seedMatchupHistory
```

#### Move to `DebugController.cs` (Development Only):
```
GET  /api/debug/analyzeTeamGames
GET  /api/debug/verifyPowerRatings
GET  /api/debug/inspectAvgScoreDeltas
GET  /api/debug/correlationAnalysis
POST /api/debug/testPredictionComponents
```

#### Archive to `LegacyController.cs` (Deprecated):
```
GET  /api/legacy/calculateCorrelation        ← Old logic
POST /api/legacy/oldMetricCalculation        ← Replaced by new system
```

### Implementation Steps:
1. Create new controller files
2. Move endpoints (copy-paste with comments)
3. Update `WORKFLOW_ROADMAP.md` with new structure
4. Add `[ApiExplorerSettings(IgnoreApi = true)]` to debug endpoints
5. Add authentication/authorization to admin endpoints

---

## Priority 3: MAUI Mobile Front End 📱
**Status:** Low Priority - New Feature  
**Estimated Time:** 4-8 hours initial setup  
**Impact:** Mobile access for predictions

### Project Scope: "NCAA Power Ratings Mobile"

#### Target Platforms:
- Android (primary)
- iOS (secondary)

#### Core Features (MVP):
1. **Prediction Screen**
   - Team 1 dropdown (autocomplete)
   - Team 2 dropdown (autocomplete)
   - Location picker (Home/Away/Neutral)
   - Week picker (1-20)
   - "Predict" button
   - Results display (score, margin, confidence)

2. **Rankings Screen**
   - Scrollable list of top 25 teams
   - Filter by year
   - Sort by Ranking, PowerRating, SOS

3. **Team Detail Screen**
   - Record (Wins-Losses)
   - Power Rating, Ranking, SOS
   - Schedule (past games with results)

#### Project Setup:
```powershell
# Create MAUI project
cd D:\Development\SaturdayPulse
dotnet new maui -n SaturdayPulse.Mobile
cd SaturdayPulse.Mobile
dotnet add reference ..\SaturdayPulse\SaturdayPulse.csproj
```

#### Tech Stack:
- **.NET MAUI** (cross-platform)
- **CommunityToolkit.Mvvm** (MVVM helpers)
- **RestSharp** or **HttpClient** (API calls to `http://localhost:5086`)
- **CommunityToolkit.Maui** (UI controls)

#### API Integration:
```csharp
public class PredictionService
{
    private readonly HttpClient _httpClient;
    private const string BaseUrl = "http://localhost:5086/api/gamedata";

    public async Task<GamePrediction> PredictMatchup(string team1, string team2, char location, int week)
    {
        var url = $"{BaseUrl}/predictMatchup?year=2025&week={week}&teamName={team1}&opponentName={team2}&location={location}";
        var response = await _httpClient.GetAsync(url);
        return await response.Content.ReadFromJsonAsync<GamePrediction>();
    }
}
```

#### UI Mockup:
```
┌─────────────────────────┐
│  NCAA Power Ratings     │
├─────────────────────────┤
│ [Prediction] [Rankings] │
├─────────────────────────┤
│                         │
│  Team 1: [Ohio State ▼] │
│  Team 2: [Michigan   ▼] │
│  Location: ⚫Home ⚪Away │
│            ⚪Neutral     │
│  Week: [15]             │
│                         │
│      [PREDICT GAME]     │
│                         │
│  ┌───────────────────┐  │
│  │ Ohio State  31.1  │  │
│  │ Michigan    11.9  │  │
│  │ Margin: OSU by 12 │  │
│  │ Confidence: Low   │  │
│  └───────────────────┘  │
└─────────────────────────┘
```

---

## Priority 4: Angular Front End 🌐
**Status:** Low Priority - New Feature  
**Estimated Time:** 8-16 hours initial setup  
**Impact:** Full-featured web application

### Project Scope: "NCAA Power Ratings Web App"

#### Target:
- Modern single-page application (SPA)
- Desktop/tablet browser experience
- Full access to all prediction features

#### Core Features (Full Functionality):
1. **Dashboard**
   - Current top 25 rankings
   - Recent predictions
   - System stats (total games, accuracy, etc.)

2. **Prediction Tool**
   - Advanced matchup builder
   - Multiple predictions at once (batch)
   - Historical prediction lookup
   - Prediction comparison (what-if scenarios)

3. **Rankings & Metrics**
   - Interactive sortable table (Ranking, PR, SOS, Win%, etc.)
   - Filter by conference, division
   - Charts/graphs (Power Rating distribution, SOS heatmap)

4. **Team Analysis**
   - Deep-dive team page
   - Schedule with predicted vs actual scores
   - Power Rating trend over season
   - Strength of schedule breakdown
   - Rival matchup history

5. **Admin Tools**
   - Load week data
   - Recalculate metrics
   - Inspect buckets
   - System health dashboard

#### Project Setup:
```powershell
# Create Angular project
cd D:\Development\SaturdayPulse
ng new ncaa-power-ratings-web --routing --style=scss
cd ncaa-power-ratings-web
npm install @angular/material
npm install chart.js ng2-charts
npm install ngx-datatable
```

#### Tech Stack:
- **Angular 18+** (TypeScript)
- **Angular Material** (UI components)
- **Chart.js** (data visualization)
- **RxJS** (reactive programming)
- **HttpClient** (API calls)

#### Project Structure:
```
ncaa-power-ratings-web/
├── src/
│   ├── app/
│   │   ├── components/
│   │   │   ├── dashboard/
│   │   │   ├── prediction/
│   │   │   ├── rankings/
│   │   │   ├── team-detail/
│   │   │   └── admin/
│   │   ├── services/
│   │   │   ├── prediction.service.ts
│   │   │   ├── rankings.service.ts
│   │   │   └── team.service.ts
│   │   ├── models/
│   │   │   ├── game-prediction.model.ts
│   │   │   ├── team-record.model.ts
│   │   │   └── ranking.model.ts
│   │   └── app-routing.module.ts
│   └── assets/
│       └── team-logos/
```

#### API Service Example:
```typescript
@Injectable({ providedIn: 'root' })
export class PredictionService {
  private apiUrl = 'http://localhost:5086/api/gamedata';

  constructor(private http: HttpClient) {}

  predictMatchup(request: MatchupRequest): Observable<GamePrediction> {
    const params = new HttpParams()
      .set('year', request.year.toString())
      .set('week', request.week.toString())
      .set('teamName', request.teamName)
      .set('opponentName', request.opponentName)
      .set('location', request.location);

    return this.http.get<GamePrediction>(`${this.apiUrl}/predictMatchup`, { params });
  }

  predictBatch(requests: MatchupRequest[]): Observable<PredictionsResponse> {
    return this.http.post<PredictionsResponse>(`${this.apiUrl}/predictMatchups`, {
      year: 2025,
      matchups: requests
    });
  }
}
```

#### UI Mockup (Dashboard):
```
┌────────────────────────────────────────────────────────────┐
│  NCAA Power Ratings                    [Predict] [Rankings]│
├────────────────────────────────────────────────────────────┤
│                                                             │
│  Top 10 Rankings                    Recent Predictions     │
│  ┌──────────────────────┐           ┌──────────────────┐  │
│  │ 1. Ohio State  0.324 │           │ OSU vs UM        │  │
│  │ 2. Georgia     0.298 │           │ Predicted: OSU 31│  │
│  │ 3. Texas       0.276 │           │ Actual: OSU 27   │  │
│  │ 4. Alabama     0.265 │           │ Accuracy: 85%    │  │
│  │ 5. Oregon      0.248 │           └──────────────────┘  │
│  └──────────────────────┘                                  │
│                                                             │
│  Power Rating Distribution          SOS Heatmap            │
│  ┌──────────────────────┐           ┌──────────────────┐  │
│  │       ▂▅▇█▇▅▂         │           │ [Interactive Map]│  │
│  │     ▁▃▆████▆▃▁       │           │ Big Ten: 0.95    │  │
│  │  ▁▂▅██████████▅▂▁    │           │ SEC: 0.92        │  │
│  └──────────────────────┘           └──────────────────┘  │
└────────────────────────────────────────────────────────────┘
```

---

## Recommended Order of Execution

### Day 1 (4-6 hours):
1. **Fix over-prediction** (1-2 hours) → Immediate impact
2. **Controller cleanup** (2-3 hours) → Sets up clean API for front-ends

### Day 2 (4-6 hours):
3. **MAUI Mobile** (4-6 hours) → Quick wins, mobile access

### Day 3-4 (8-12 hours):
4. **Angular Web App** (8-12 hours) → Full-featured experience

---

## Pre-Work Checklist

### Before Starting Tomorrow:
- [ ] Commit current work to Git (`Development` branch)
- [ ] Create feature branch: `git checkout -b feature/prediction-accuracy-fix`
- [ ] Backup database (SQLite file)
- [ ] Document current prediction accuracy baseline (67%, +10 point error)
- [ ] Review `GamePredictionService.cs` (line ~150-300)

### Dependencies to Install (for MAUI/Angular):
```powershell
# MAUI workload
dotnet workload install maui

# Angular CLI
npm install -g @angular/cli

# Verify installations
dotnet workload list
ng version
```

---

## Success Metrics

### Priority 1 Success:
- [ ] Total over-prediction reduced from +10 to +3-5 points
- [ ] OSU-UM total within 3 points of actual (36)
- [ ] TX-OU total within 5 points of actual (29)
- [ ] Overall accuracy improves from B+ (67%) to A- (75%+)

### Priority 2 Success:
- [ ] Production endpoints clean and documented
- [ ] Admin endpoints separated and secured
- [ ] Debug endpoints marked as development-only
- [ ] Swagger UI shows clear API organization

### Priority 3 Success:
- [ ] MAUI app runs on Android emulator
- [ ] Can select teams and predict matchup
- [ ] Results display correctly
- [ ] Rankings screen shows top 25

### Priority 4 Success:
- [ ] Angular app runs on `localhost:4200`
- [ ] Dashboard loads rankings
- [ ] Prediction tool functional
- [ ] Team detail page shows full stats

---

## Notes & Reminders

### API Base URL:
- Development: `http://localhost:5086/api/gamedata`
- Production: TBD (consider Azure deployment)

### Current System State:
- Database: SQLite (`SaturdayPulse.db`)
- 5% win-percentage bucketing (208 buckets)
- 2025 season loaded through week 20
- Prediction accuracy: 67% pick rate, +10 point over-prediction

### Git Workflow:
```powershell
# Feature branch for each priority
git checkout -b feature/fix-over-prediction
git commit -am "Fix over-prediction with 0.92x multiplier"
git push origin feature/fix-over-prediction

# Repeat for each feature
```

---

## Questions to Answer Tomorrow

1. Should we apply 0.92× multiplier globally or only to specific game types?
2. Do we want authentication on admin endpoints? (Yes for production)
3. Should MAUI app connect to localhost or deployed API?
4. Do we need team logos for mobile/web apps? (Find asset source)
5. Should Angular app have dark mode? (Yes, easy with Material)

---

**Ready to rock! 🚀 Pick your starting point tomorrow and let's ship some features!**
