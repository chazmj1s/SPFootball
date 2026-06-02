# Feature Ideas & Product Backlog

Last updated: 2026-06-02
Project: NCAA Power Ratings (.NET 9 API + .NET MAUI app + SaturdayPulse.Admin console)

## How to Use This File
- Keep ideas here until they're promoted to implementation tasks.
- Use `Status`:
  - `Idea` = rough concept
  - `Planned` = agreed and scoped
  - `In Progress` = actively building
  - `Blocked` = waiting on dependency
  - `Done` = shipped
- Add a short acceptance checklist when moving to `Planned`.

---

## Priority Snapshot

### Now
- Bug C — FollowGameIcon / FollowTeamIcon not persisting (favoriting broken)
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
- Idea 19 — Era-Normalized Scoring (AvgScoreDifferential)
- Idea 20 — Game Metrics Expand Panel (Scores/Schedule tab)

---

## Backlog

---

### Idea 1 — Preseason Power Ratings via Rolling Average
**Status:** In Progress
**Type:** Backend + Admin
**Summary:**
Use last 10 seasons to project preseason ratings. Rolling average of PPG, PAG, win pct,
power rating. Seed TeamRecords before season starts. Week 7 switchover blends current
season data in.
**What exists:**
- `TeamMetricsService.CalculateProjectedWins` already computes 10-year weighted average
  and drives SOS seeding at week 0
- `FifoDoubleQueue` (capacity 10, most-recent-weighted) already built for rolling average
- `RollingAverageService` built and wired into backfill pipeline
- `calculateRollingAverages` and `backfillRollingAverages` endpoints operational in admin console
**What's missing:**
- Admin endpoint: `GET /preseason-ratings?year=` — returns projected ratings
- Admin endpoint/job to seed `TeamRecords` with rolling average projections for upcoming year
- Week 7 switchover logic in `GamePredictionService` to blend prior-year and
  current-year data
**Notes:** Foundational for realistic early-season projections and trend graphs.
Also resolves the early-season ranking noise (e.g. Vanderbilt at #1 after 4 games)
by giving every team a meaningful prior expectation from day one.

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
Use projected conference champions + at-large rankings to seed 12-team playoff bracket.
Apply CFP autobid rules in real time.
**Notes:** Pairs with Ideas 2, 5, and 6.

---

### Idea 4 — Following Tab (Replaces Teams + Rivalries)
**Status:** Idea
**Type:** MAUI UX
**Summary:**
Toggle/dropdown: Teams | Games | Rivalries. Gold `+` above Wk on game card to follow a
game. Followed games pinned at top of Following → Games above EPIC tier. Followed games
tier icon = gold `+`. Tapping team/game opens expanded detail card.
**Notes:** Strong user-retention feature. `FollowTeamIcon` and `FollowGameIcon` controls
already built. Blocked by Bug C (favoriting not persisting).

---

### Idea 5 — Projections Tab — Playoffs View
**Status:** Idea
**Type:** MAUI/Web UX + Projection
**Summary:**
Add Playoffs view alongside Standings and Title Game. Tab order:
`Standings | Title Game | Playoffs`. Apply CFP autobid rules to project full bracket.
Projected games show `P` badge.
**Notes:** `postseason/v2` endpoint now splits `PlayoffDays` and `BowlDayGroup` by
`SeasonType`. Admin console postseason tagging page operational for manually flagging
CFP games. Dependency on Idea 2/3 for forward-looking bracket projection.

---

### Idea 6 — Projections Tab — Playoff Game Projections
**Status:** Idea
**Type:** Projection + UI
**Summary:**
When CFP bracket is set, add projected scores for each playoff game. Use existing
`GamePredictionService`. Update as bracket advances.

---

### Idea 7 — Projections Tab — Sandbox
**Status:** Idea
**Type:** UX + Projection
**Summary:**
Mix and match any two teams at current season state. Show projected outcome. Sandbox
games can be followed with `P` badge.
**Notes:** `sandbox/predict` endpoint operational (`GET /api/productiongamedata/sandbox/predict`).
Frontend implementation still needed.

---

### Idea 8 — Conference Table in DB
**Status:** Planned
**Type:** Data Model / Architecture
**Summary:**
Add dedicated `Conference` table with FK from `Team`. Fields: name, abbreviation, tier,
division format, membership status (active/historical), effective date range. Remove
fragile string matching throughout codebase. Support historical conference switches
(e.g. Texas/OU to SEC, Pac-12 collapse).
**Acceptance:**
- [ ] `Conference` model and migration
- [ ] FK added to `Team`
- [ ] `GetConferenceTier` replaced with DB lookup
- [ ] Historical membership rows for teams that switched conferences
- [ ] `WeeklyRankingsService` and controllers updated to use FK
**Notes:** `GetConferenceTier` was patched to match full conference name strings
(e.g. "Southeastern Conference") — that fix works but this is the real solution.
`TeamsConferenceHistory` table already tracks historical affiliation — this idea
extends that with a proper Conference entity.

---

### Idea 9 — Global/Per-Tab Navigation Config
**Status:** Idea
**Type:** MAUI App Architecture
**Summary:**
`NavigationConfigService` singleton stores `IsWeekGlobal`, `IsYearGlobal`.
`SharedNavigationState` holds global year/week. Each ViewModel checks config before
reacting to nav changes. Config tab UI includes toggles.
**Notes:** Quality-of-life booster and prerequisite for multi-sport expansion.

---

### Idea 10 — Rankings throughWeek Support
**Status:** Done ✓
**Type:** API Enhancement
**Notes:** Shipped. `WeeklyRankings` table fully backfilled 1965–2025. `throughWeek`
parameter operational. Offense/Defense expand panel in Rankings tab. See decision log.

---

### Idea 11 — Trend Graphs + Historical Context
**Status:** Idea
**Type:** Analytics (Web-first, MAUI secondary)
**Summary:**
Per-team season arc visualization: overall rank, power rating, and SOS movement by week.
Overlay current season against prior year(s) at the same week. Projected trajectory as
dashed line past current week. Conference-switch annotations on historical charts.
**Data available:**
- 61 years of `WeeklyRankings` (1965–2025) fully backfilled
- 10-year rolling average computed in `TeamMetricsService`
- `teamseason` endpoint returns week-by-week arc data per team/year
- `calculateTrends` endpoint exists but is a placeholder — full implementation needed
**Notes:** High value-add differentiator. Admin console Analytics page is the natural
home for a prototype before MAUI implementation.

---

### Idea 12 — Poll Data Integration
**Status:** Idea
**Type:** Data Integration
**Summary:**
Integrate AP Poll and Coaches Poll alongside power ratings. Highlight model-vs-poll
divergence (teams the model rates higher/lower than human voters).

---

### Idea 13 — Vegas Odds Integration
**Status:** Idea
**Type:** Data Integration + Monetization
**Summary:**
Use free API for Sun/Wed updates. Store odds in `Game` table. Compare model projection
vs Vegas line. Premium unlock candidate.
**Notes:** Vegas closing/opening line data already flowing through CFBD into projections.
Full real-time odds integration is the next step.

---

### Idea 14 — Freemium Monetization
**Status:** Idea
**Type:** Product / Monetization
**Summary:**
Free: Scores, Rankings, Teams, Rivalries.
Premium ($0.99): projected scores, margins, O/U, Vegas odds. Parenthetical `( )` format
already built in UI.

---

### Idea 15 — Azure Deployment + CI/CD
**Status:** Idea
**Type:** DevOps / Platform
**Summary:**
API to Azure App Service. GitHub Actions CI/CD on push to main. React+TypeScript web
frontend to Azure Static Web Apps. Migrate SQLite to Azure SQL (rename singular tables
to plural convention at this point).
**Notes:** SQLite locking issues observed under concurrent load — Azure SQL migration
is the correct fix before real user traffic. Table rename (`Game` → `Games`,
`Team` → `Teams`) planned for migration. Admin console (Angular) is a candidate for
Azure Static Web Apps alongside the public web frontend.

---

### Idea 16 — Expanded Game Metrics Panel (Scores Tab)
**Status:** Idea
**Type:** UX + Premium Surface
**Summary:**
Tap a game in Scores to expand panel with projected metrics (projected scores, margin,
O/U, confidence, future Vegas comparison). Collapse on second tap. Free tier sees actual
scores only; premium unlock shows projections.

---

### Idea 17 — Paywall Implementation for Projections
**Status:** Idea
**Type:** Monetization / Platform
**Summary:**
Implement $0.99 unlock flow for projection features. Includes store IAP integration
(App Store/Google Play), paywall gates in Scores panel, Projections tab, and future
Vegas odds. Free tier shows `( )`; premium reveals values. Consider trial/preview mode.
**Notes:** ASP.NET Core Identity + JWT Bearer auth + role-based authorization needed
first. Stripe is the recommended billing integration.

---

### Idea 18 — SSAS Analytics Layer
**Status:** Idea
**Type:** Analytics / Platform (Azure migration dependency)
**Summary:**
Deploy SQL Server Analysis Services on Azure alongside SQL Server migration. Build OLAP
cubes over `WeeklyRankings` and `TeamRecords` for pre-aggregated historical analytics.
Enables MDX-driven trend projections, year-over-year comparisons, and rolling multi-year
program trajectory without C# computation overhead.
**Potential cube dimensions:** Team, Conference, Year, Week, Tier
**Potential measures:** Ranking, PowerRating, CombinedSOS, Wins, Losses, WinPct,
RankMovement, OffensiveZScore, DefensiveZScore
**Notes:** Complements (not replaces) the custom prediction engine. Requires SQL Server
Enterprise or Developer edition. High lift, high payoff. Azure SQL migration is a
hard prerequisite.

---

### Idea 19 — Era-Normalized Scoring in AvgScoreDifferential
**Status:** Idea
**Type:** Projection Engine
**Summary:**
`AvgScoreDifferential` is currently a single flat table with no concept of era.
Lower-scoring pre-1980 games pull the baseline down, contributing to the consistent
~-3pt spread bias seen in projection accuracy reporting. Potential approaches:
1. **Reporting-only normalization** — add `normalizedMAE` (error as % of total scoring)
   to `projectionAccuracy` endpoint for era-aware benchmarking without touching the model.
2. **Era-bucketed differentials** — segment `AvgScoreDifferential` by era
   (pre-1980, 1980-2000, 2000-2015, 2015+) so lookups adapt to scoring inflation.
**Risk:** Era bucketing splits a statistically significant 60-year dataset into smaller,
noisier buckets — could hurt modern projections more than the fix helps. Requires
holdout testing before committing.
**Recommended first step:** Option 1 (reporting normalization) — zero model risk,
immediate analytical value. Option 2 is a future investigation item pending test results.
**Notes:** Current 69.6% winner accuracy and consistent (not variable) bias suggest the
model is healthy. Do not change what's working without data to support the change.

---

### Idea 20 — Game Metrics Expand Panel (Scores / Schedule Tab)
**Status:** Idea
**Type:** MAUI UX + API
**Summary:**
Tap a chevron on a Scores/Schedule game card to expand an inline metrics panel —
same interaction pattern as the Offense/Defense expand panel on the Rankings tab.
Panel shows pertinent metrics and projections for that specific game without
navigating away from the list.
**Suggested panel content:**
- Projected scores (home / away) with margin
- Over/Under projection vs Vegas line (if available)
- Confidence rating
- Home and away Power Rating + SOS at time of game
- Home and away Pedigree / Trend / Seed ratings
- Rivalry note (if applicable)
- For played games: actual result vs projected, spread delta
**Implementation notes:**
- Follows the same chevron-toggle + DataTrigger pattern used in Rankings
- `predictMatchup` endpoint (`GET /api/productiongamedata/predictMatchup`) already
  returns projected scores, margin, confidence, rivalry note, and summary
- `ProjectionCacheService` / `Projections` table has pre-computed projections
  available for historical and current games — prefer cached data over live calls
- Free tier: show actual scores only; premium unlock reveals projections
  (aligns with Idea 14 freemium model and Idea 16)
- Panel should collapse on second tap, same as Rankings
**Acceptance:**
- [ ] Chevron toggle added to game card in Scores/Schedule XAML
- [ ] Expand panel XAML with metrics layout
- [ ] ViewModel wires up projection data (cached preferred, live fallback)
- [ ] Played games show actual vs projected comparison
- [ ] Unplayed games show projection only
- [ ] Premium gate applied to projection fields (free tier sees blanks or `( )`)
**Notes:** Pairs with Idea 16 (Expanded Game Metrics Panel) which describes the
same feature from the premium surface perspective. These can be built together.

---

## Known Bugs

### Bug A — Alternating row shading wrong when filtered
**Status:** Done ✓
**Fix:** Re-stamped `IsOddRow` based on filtered position index in `ApplyFiltersAndSort`.

### Bug B — Data loading through AbsoluteLayout page host
**Status:** Open — verify next session
**Fix direction:** Re-test navigation + load lifecycle in page host and confirm event
ordering.

### Bug C — FollowGameIcon / FollowTeamIcon not persisting
**Status:** Open
**Symptoms:** Tapping the star/heart to follow a game or team appears to work in the UI
but the state does not persist across navigation or app restart. Follow state reverts
on return.
**Fix direction:**
- Verify the follow/unfollow API call is firing and returning success
- Check whether `IsFollowed` is being written back to local state after the API response
- Confirm the ViewModel is re-querying followed state on page load/navigation
- Check `FollowIcon` DataTrigger binding — may be one-time rather than reactive
**Notes:** `FollowGameIcon` (★/☆, Gold/Gray) and `FollowTeamIcon` (♥/♡, #FF6666/#666666)
controls are built. The visual toggle works; persistence is the failure point. Blocks Idea 4.

---

## Decision Log
- 2026-06-02: Era-normalized scoring added as Idea 19. Consensus: reporting-only
  normalization first, model change only after holdout test validates improvement.
  Current spread bias (-3.11) is consistent and acceptable; do not break what works.
- 2026-06-02: Admin console (SaturdayPulse.Admin) scaffolded as Angular 19 + Angular
  Material dark theme. Pages: Dashboard, Data Operations, Postseason Tagging, Metrics
  Rebuild, Analytics. All major data pipeline operations accessible via console.
- 2026-06-02: Postseason tagging workflow implemented — `tagAsPlayoff` /
  `untagAsPlayoff` endpoints added to DeveloperController. Admin console postseason
  page supports load, checklist tag, and save in one workflow.
- 2026-05-07: `updateWeekGames` (web fetch) and `updateWeekGamesFromFile` (local file)
  intentionally kept as separate endpoints until a reliable live data source is confirmed.
  Previous source blacklisted the server after a 60-year bulk scrape.
- 2026-05-07: `computeweekly` endpoint noted as misplaced in `ProductionGameDataController`
  — migrate to `DeveloperController` when gateway pattern is implemented.
- 2026-05-07: API gateway pattern documented as future architecture. Prerequisites:
  ASP.NET Core Identity, JWT Bearer auth, role-based authorization, Stripe pay wall.
- 2026-05-07: Offensive/Defensive Z-scores decomposed from composite Z-score using
  symmetric margin split around league average. Stored as `OffensiveZScore`,
  `DefensiveZScore`, `OffensiveRank`, `DefensiveRank` in both tables.
- 2026-05-07: Early season ranking noise (weeks 1-6) acknowledged as known limitation.
  Fix deferred to Idea 1 rolling average seeding — not a bug, expected behavior
  with sparse data.
- 2026-05-06: Chose pre-computed `WeeklyRankings` table over on-the-fly calculation
  for throughWeek rankings — performance and snapshot integrity.
- 2026-05-06: SQLite table names kept singular (`Game`, `Team`) until Azure SQL
  migration — rename planned at that point.
- 2026-05-06: `GetConferenceTier` patched to match full conference name strings;
  proper fix deferred to Idea 8 Conference table.
