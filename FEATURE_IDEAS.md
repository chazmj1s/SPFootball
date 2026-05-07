# Feature Ideas & Product Backlog

Last updated: 2026-05-06
Project: NCAA Power Ratings (.NET 9 API + .NET MAUI app + web console)

## How to Use This File
- Keep ideas here until they're promoted to implementation tasks.
- Use `Status`:
  - `Idea` = rough concept
  - `Planned` = agreed and scoped
  - `In Progress` = actively building
  - `Blocked` = waiting on dependency
  - `Done` = shipped

---

## Priority Snapshot

### Now
- Idea 8 — Conference Table in DB (fragile string matching is a recurring pain point)
- Idea 1 — Preseason Power Ratings (engine exists, needs seeding endpoint)
- Idea 4 — Following Tab
- Bug B — AbsoluteLayout data loading verification

### Next
- Idea 5 — Projections Tab Playoffs View
- Idea 6 — Playoff Game Projections
- Idea 9 — Global/Per-Tab Navigation Config
- Idea 11 — Trend Graphs + Historical Context

### Later
- Ideas 12–17 (integrations, monetization, deployment)
- Idea 18 — SSAS Analytics Layer (Azure migration dependency)

---

## Backlog

---

### Idea 1 — Preseason Power Ratings via Rolling Average
**Status:** In Progress
**Type:** Backend + Admin
**Summary:**
Use last 10 seasons to project preseason ratings. Rolling average of PPG, PAG, win pct, power rating. Seed TeamRecords before season starts. Week 7 switchover blends current season data in.
**What exists:**
- `TeamMetricsService.CalculateProjectedWins` already computes 10-year weighted average and drives SOS seeding at week 0
**What's missing:**
- Admin endpoint to trigger preseason `TeamRecords` population
- Blend logic at week 7 switchover
**Notes:** Foundational for realistic early-season projections and trend graphs.

---

### Idea 2 — In-Season Projection Through Conference Championships
**Status:** Idea
**Type:** Projection Engine
**Summary:**
Run projection algorithm through full season including conference championship outcomes.
**Notes:** Dependency for realistic playoff seeding (Ideas 3, 5, 6).

---

### Idea 3 — Projected CFP Seeding
**Status:** Idea
**Type:** Projection Engine + Rules
**Summary:**
Use projected conference champions + at-large rankings to seed 12-team playoff bracket. Apply CFP autobid rules in real time.
**Notes:** Pairs with Ideas 2, 5, and 6.

---

### Idea 4 — Following Tab (Replaces Teams + Rivalries)
**Status:** Idea
**Type:** MAUI UX
**Summary:**
Toggle/dropdown: Teams | Games | Rivalries. Gold `+` above Wk on game card to follow a game. Followed games pinned at top of Following → Games above EPIC tier. Followed games tier icon = gold `+`. Tapping team/game opens expanded detail card.
**Notes:** Strong user-retention feature. `FollowTeamIcon` and `FollowGameIcon` controls already built.

---

### Idea 5 — Projections Tab — Playoffs View
**Status:** Idea
**Type:** MAUI/Web UX + Projection
**Summary:**
Add Playoffs view alongside Standings and Title Game. Tab order: `Standings | Title Game | Playoffs`. Apply CFP autobid rules to project full bracket. Projected games show `P` badge.

---

### Idea 6 — Projections Tab — Playoff Game Projections
**Status:** Idea
**Type:** Projection + UI
**Summary:**
When CFP bracket is set, add projected scores for each playoff game. Use existing `GamePredictionService`. Update as bracket advances.

---

### Idea 7 — Projections Tab — Sandbox
**Status:** Idea
**Type:** UX + Projection
**Summary:**
Mix and match any two teams at current season state. Show projected outcome. Sandbox games can be followed with `P` badge.

---

### Idea 8 — Conference Table in DB
**Status:** Planned
**Type:** Data Model / Architecture
**Summary:**
Add dedicated `Conference` table with FK from `Team`. Fields: name, abbreviation, tier, division format, membership status (active/historical), effective date range. Remove fragile string matching throughout codebase. Support historical conference switches (e.g. Texas/OU to SEC, Pac-12 collapse).
**Acceptance:**
- [ ] `Conference` model and migration
- [ ] FK added to `Team`
- [ ] `GetConferenceTier` replaced with DB lookup
- [ ] Historical membership rows for teams that switched conferences
- [ ] `WeeklyRankingsService` and controller updated to use FK
**Notes:** We patched `GetConferenceTier` to match full conference name strings today — that fix works but this is the real solution. High-value structural fix that unblocks clean trend graphs and analytics.

---

### Idea 9 — Global/Per-Tab Navigation Config
**Status:** Idea
**Type:** MAUI App Architecture
**Summary:**
`NavigationConfigService` singleton stores `IsWeekGlobal`, `IsYearGlobal`. `SharedNavigationState` holds global year/week. Each ViewModel checks config before reacting to nav changes. Config tab UI includes toggles.
**Notes:** Quality-of-life booster and prerequisite for multi-sport expansion.

---

### Idea 10 — Rankings throughWeek Support
**Status:** Done
**Type:** API Enhancement
**Summary:**
Added `throughWeek` parameter to power rankings endpoint. Pre-computed `WeeklyRankings` table populated by `WeeklyRankingsService` pipeline (SOS → PowerRating → Ranking) scoped to games through selected week. Mobile app passes `SelectedWeek` through to API.
**What shipped:**
- `WeeklyRanking` model + `WeeklyRankings` table (SQLite, manually created)
- `WeeklyRankingsService.ComputeAndSaveAsync` — full SOS/PowerRating/Ranking pipeline per week
- `WeeklyRankingsService.BackfillYearAsync` — single year backfill
- `DeveloperController.BackfillWeeklyRankings` — full historical backfill across all years/weeks
- `GetPowerRankings` updated to query `WeeklyRankings` when `throughWeek` provided, `TeamRecords` otherwise
- `PowerRankingsViewModel` now fires reload on `SelectedWeek` change
- Conference tier detection fixed for full conference name strings (e.g. "Southeastern Conference")

---

### Idea 11 — Trend Graphs + Historical Context
**Status:** Idea
**Type:** Analytics (Web-first, MAUI secondary)
**Summary:**
Per-team season arc visualization: overall rank, power rating, and SOS movement by week. Overlay current season against prior year(s) at the same week. Projected trajectory as dashed line past current week. Conference-switch annotations on historical charts.
**Data available:**
- 61 years of `WeeklyRankings` (1965–2025) fully backfilled
- 10-year rolling average already computed in `TeamMetricsService`
- Projection engine available via `GamePredictionService`
**Notes:** Proof-of-concept chart built for Texas 2025 — rank, power rating, and SOS arc clearly shows team story. High value-add for users; differentiator vs ESPN/247Sports.

---

### Idea 12 — Poll Data Integration
**Status:** Idea
**Type:** Data Integration
**Summary:**
Integrate AP Poll and Coaches Poll alongside power ratings. Highlight model-vs-poll divergence (teams the model rates higher/lower than human voters).

---

### Idea 13 — Vegas Odds Integration
**Status:** Idea
**Type:** Data Integration + Monetization
**Summary:**
Use free API for Sun/Wed updates. Store odds in `Game` table. Compare model projection vs Vegas line. Premium unlock candidate.

---

### Idea 14 — Freemium Monetization
**Status:** Idea
**Type:** Product / Monetization
**Summary:**
Free: Scores, Rankings, Teams, Rivalries.
Premium ($0.99): projected scores, margins, O/U, Vegas odds. Parenthetical `( )` format already built in UI.

---

### Idea 15 — Azure Deployment + CI/CD
**Status:** Idea
**Type:** DevOps / Platform
**Summary:**
API to Azure App Service. GitHub Actions CI/CD on push to main. React+TypeScript web frontend to Azure Static Web Apps. Migrate SQLite to Azure SQL (rename singular tables to plural convention at this point).
**Notes:** Table rename (`Game` → `Games`, `Team` → `Teams`) planned for this migration to fix naming convention debt.

---

### Idea 16 — Expanded Game Metrics Panel (Scores Tab)
**Status:** Idea
**Type:** UX + Premium Surface
**Summary:**
Tap a game in Scores to expand panel with projected metrics (projected scores, margin, O/U, confidence, future Vegas comparison). Collapse on second tap. Free tier sees actual scores only; premium unlock shows projections.

---

### Idea 17 — Paywall Implementation for Projections
**Status:** Idea
**Type:** Monetization / Platform
**Summary:**
Implement $0.99 unlock flow for projection features. Includes store IAP integration (App Store/Google Play), paywall gates in Scores panel, Projections tab, and future Vegas odds. Free tier shows `( )`; premium reveals values. Consider trial/preview mode.

---

### Idea 18 — SSAS Analytics Layer
**Status:** Idea
**Type:** Analytics / Platform (Azure migration dependency)
**Summary:**
Deploy SQL Server Analysis Services on Azure alongside SQL Server migration. Build OLAP cubes over `WeeklyRankings` and `TeamRecords` for pre-aggregated historical analytics. Enables MDX-driven trend projections, year-over-year comparisons, and rolling multi-year program trajectory without C# computation overhead.
**Potential cube dimensions:** Team, Conference, Year, Week, Tier
**Potential measures:** Ranking, PowerRating, CombinedSOS, Wins, Losses, WinPct, RankMovement
**Use cases:**
- "Where is this program headed over the next 3 years" macro projections
- Conference strength trends over decades
- Historical context overlays on current season charts (Idea 11)
- Win rate by conference over time, scoring trends, upset frequency by week
**Notes:** Complements (not replaces) the custom prediction engine — SSAS handles historical trend analysis and macro projections; `GamePredictionService` handles week-by-week game predictions where rivalry adjustments and home field factors matter. Requires SQL Server Enterprise or Developer edition. High lift, high payoff.

---

## Known Bugs

### Bug A — Alternating row shading wrong when filtered
**Status:** Done
**Fix:** Re-stamp `IsOddRow` based on filtered position index in `ApplyFiltersAndSort`. Shipped.

### Bug B — Data loading through AbsoluteLayout page host
**Status:** Open — verify next session
**Fix direction:** Re-test navigation + load lifecycle in page host and confirm event ordering.

---

## Decision Log
- 2026-05-06: Chose pre-computed `WeeklyRankings` table over on-the-fly calculation for throughWeek rankings performance
- 2026-05-06: SQLite table names kept singular (`Game`, `Team`) until Azure SQL migration — rename planned at that point
- 2026-05-06: `GetConferenceTier` patched to match full conference name strings; proper fix deferred to Idea 8 Conference table