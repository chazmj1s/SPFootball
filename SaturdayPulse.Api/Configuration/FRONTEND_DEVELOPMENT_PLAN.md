# Front-End Development Plan

## 📱 MAUI Mobile App - IN PROGRESS

### Status
✅ Project created: `SaturdayPulse.Mobile`  
✅ Added to solution  
✅ API service layer created  
⚠️ MAUI workloads need to be installed (normal for first-time MAUI setup)

### Install MAUI Workloads
To complete the MAUI setup, run this in Visual Studio:
```
Tools → Command Line → Developer Command Prompt
dotnet workload install maui
```

Or via PowerShell:
```powershell
dotnet workload install maui
```

### MAUI App Features (Limited/Mobile-Focused)
1. **Game Predictions**
   - Simple form: Team vs Opponent
   - Location selector (Home/Away/Neutral)
   - Display: Score prediction, confidence, summary

2. **Rivalry Browser**
   - Filter by tier (EPIC, National, State, All)
   - List view with rivalry name, teams, series record
   - Tap for details: avg margin, upset rate, history

3. **Team Quick Search**
   - Search by team name
   - Filter by year, min wins
   - Display: Record, power rating, SOS, point differential

4. **Top 25 Rankings** (future)
   - Current year rankings
   - Sortable by different metrics
   - Refresh on pull-down

### API Service Created
✅ `PredictionApiService.cs` - Consumes Production API
- `PredictMatchupAsync()` - Game predictions
- `GetRivalriesAsync()` - Rivalry data
- `QueryTeamRecordsAsync()` - Team records

**Note:** API URL configured for:
- Android emulator: `http://10.0.2.2:5086`
- iOS simulator: `http://localhost:5086`
- You'll need to update for production deployment

### Pages to Create
1. `PredictionPage.xaml` - Main prediction form
2. `RivalriesPage.xaml` - Rivalry browser
3. `TeamSearchPage.xaml` - Team search
4. `AppShell.xaml` - Update with tabbed navigation

### MAUI Project Structure
```
SaturdayPulse.Mobile/
├── Services/
│   └── PredictionApiService.cs ✅
├── Views/
│   ├── PredictionPage.xaml (TODO)
│   ├── RivalriesPage.xaml (TODO)
│   └── TeamSearchPage.xaml (TODO)
├── ViewModels/
│   ├── PredictionViewModel.cs (TODO)
│   ├── RivalriesViewModel.cs (TODO)
│   └── TeamSearchViewModel.cs (TODO)
└── MauiProgram.cs (TODO: register services)
```

---

## 🌐 Angular Web Console - PLANNING

### Development Environment
**Recommended: VS Code** with extensions:
- Angular Language Service
- ESLint
- Prettier
- Angular Snippets
- GitLens

### Setup Steps (After MAUI Complete)
1. **Create Angular project** (VS Code)
   ```bash
   ng new ncaa-power-ratings-web --routing --style=scss
   ```

2. **Install dependencies**
   ```bash
   npm install @angular/material
   npm install chart.js ng2-charts
   npm install @ngrx/store @ngrx/effects  # state management
   ```

3. **Project structure**
   ```
   src/
   ├── app/
   │   ├── core/              # Services, interceptors
   │   ├── features/
   │   │   ├── predictions/   # Prediction module
   │   │   ├── analytics/     # Analytics dashboard
   │   │   ├── teams/         # Team queries
   │   │   ├── rivalries/     # Rivalry deep-dive
   │   │   └── admin/         # Developer endpoints
   │   ├── shared/            # Shared components
   │   └── models/            # TypeScript interfaces
   ```

### Angular App Features (Full Functionality)

#### 1. **Prediction Suite**
- Single game prediction form
- Batch prediction builder
- Historical prediction accuracy tracker
- Prediction export (CSV, JSON)

#### 2. **Analytics Dashboard**
- System-wide analytics charts
- Team performance trends
- SOS vs Win% scatter plots
- Power rating distribution
- Year-over-year comparisons

#### 3. **Advanced Team Queries**
- Multi-filter search (wins, losses, year, SOS, PR)
- Sortable data tables
- Export results
- Detailed team view with game-by-game breakdown

#### 4. **Rivalry Deep Dive**
- All 50 curated rivalries
- Series history charts
- Variance analysis
- Upset prediction models
- Head-to-head comparisons

#### 5. **Admin Console** (Developer Endpoints)
- Data loading interface
- Metric calculation triggers
- Backfill operations
- Score delta management
- System diagnostics

#### 6. **Data Visualization**
- Chart.js/ng2-charts integration
- Interactive graphs
- Trend lines
- Comparative visualizations
- Export charts as images

### API Services (Angular)
```typescript
// services/
├── prediction-api.service.ts    # Production endpoints
├── developer-api.service.ts     # Admin endpoints
├── auth.service.ts              # Authentication (future)
└── analytics.service.ts         # Data processing
```

### Angular Modules
```typescript
// app structure
├── app-routing.module.ts
├── core.module.ts              # Singleton services
├── shared.module.ts            # Shared components
├── predictions.module.ts       # Predictions feature
├── analytics.module.ts         # Analytics feature
├── teams.module.ts             # Team queries feature
├── rivalries.module.ts         # Rivalries feature
└── admin.module.ts             # Admin console feature
```

---

## 🎯 Development Order

### Phase 1: MAUI (This Week)
1. ✅ Create project and API service
2. ⏳ Install MAUI workloads
3. ⏳ Build prediction page
4. ⏳ Build rivalry browser
5. ⏳ Build team search
6. ⏳ Test on Android emulator
7. ⏳ Polish UI/UX

### Phase 2: Angular (Next Week)
1. ⏳ Setup Angular project in VS Code
2. ⏳ Create API services
3. ⏳ Build routing structure
4. ⏳ Implement prediction module
5. ⏳ Implement analytics dashboard
6. ⏳ Implement team queries
7. ⏳ Implement rivalry module
8. ⏳ Implement admin console
9. ⏳ Add charts and visualization
10. ⏳ Polish and test

---

## 🔧 Next Immediate Steps

### For MAUI (Stay in Visual Studio)
1. **Install MAUI workload:**
   ```
   Tools → Command Line → Developer Command Prompt
   dotnet workload install maui
   ```

2. **Update MauiProgram.cs** to register HttpClient and PredictionApiService

3. **Create ViewModels** with MVVM pattern

4. **Build XAML pages** with data binding

5. **Test on Android emulator**

### For Angular (Later, switch to VS Code)
1. Install Node.js (if not installed)
2. Install Angular CLI: `npm install -g @angular/cli`
3. Create new workspace
4. Install dependencies
5. Start building modules

---

## 📦 Repository Structure

```
SaturdayPulse/
├── SaturdayPulse/              # Backend API (.NET 9)
│   ├── Controllers/
│   │   ├── ProductionGameDataController.cs
│   │   └── DeveloperController.cs
│   ├── Services/
│   └── Models/
├── SaturdayPulse.Mobile/       # MAUI App (.NET 9)
│   ├── Services/
│   ├── Views/
│   └── ViewModels/
└── (separate repo)
    └── ncaa-power-ratings-web/      # Angular App
        ├── src/
        ├── angular.json
        └── package.json
```

**Note:** Angular app will likely be a **separate Git repository** to keep concerns separated and allow independent deployment.

---

## 🎨 Technology Stack Summary

| Layer | Technology | Purpose |
|-------|-----------|---------|
| Backend API | .NET 9 / ASP.NET Core | RESTful API |
| Mobile App | .NET MAUI | Cross-platform mobile |
| Web Console | Angular 19+ | Full-featured web app |
| Database | SQLite | Embedded database |
| Charts | Chart.js / ng2-charts | Data visualization |
| State Mgmt | NgRx (optional) | Angular state management |

---

## ✅ Ready to Continue

**Current Focus: MAUI**

Once you install the MAUI workload, I can:
1. Update `MauiProgram.cs` to wire up services
2. Create ViewModels with INotifyPropertyChanged
3. Build the three main XAML pages
4. Set up tabbed navigation in AppShell
5. Add styling and polish

**Let me know when the workload is installed and we'll continue!** 📱
