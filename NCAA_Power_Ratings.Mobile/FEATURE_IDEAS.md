# Feature Ideas & Product Backlog

Last updated: 2026-05-06  
Project: NCAA Power Ratings (.NET 9 API + .NET MAUI app + web console)

## How to Use This File
- Keep ideas here until they’re promoted to implementation tasks.
- Use `Status`:
  - `Idea` = rough concept
  - `Planned` = agreed and scoped
  - `In Progress` = actively building
  - `Blocked` = waiting on dependency
  - `Done` = shipped
- Add a short acceptance checklist when moving to `Planned`.

---

## Priority Snapshot (Suggested)
### Now
- Idea 10 — Rankings throughWeek Support
- Idea 8 — Conference Table in DB
- Idea 1 — Preseason Power Ratings via Rolling Average
- Known bug: filtered row shading
- Known bug: AbsoluteLayout host data-loading verification

### Next
- Idea 4 — Following Tab
- Idea 5 — Projections Tab Playoffs View
- Idea 6 — Playoff Game Projections
- Idea 9 — Global/Per-Tab Navigation Config

### Later
- Ideas 11–17 (integrations, monetization, deployment expansion)

---

## Backlog

## Idea 1 — Preseason Power Ratings via Rolling Average
**Status:** Idea  
**Type:** Backend + Admin  
**Summary:**  
Use last 10 seasons to project preseason ratings. Rolling average of PPG, PAG, win pct, power rating. Seed TeamRecords before season. Week 7 switchover to blend current season.  
**Needs:**  
- RollingAverageService  
- preseason-ratings endpoint  
- admin seeding job  
**Notes:** This is foundational for more realistic early-season projections.

---

## Idea 2 — In-Season Projection Through Conference Championships
**Status:** Idea  
**Type:** Projection Engine  
**Summary:**  
Run projection algorithm through full season including conference championship outcomes.  
**Notes:** Dependency for realistic playoff seeding workflows.

---

## Idea 3 — Projected CFP Seeding
**Status:** Idea  
**Type:** Projection Engine + Rules  
**Summary:**  
Use projected conference champions + at-large rankings to seed 12-team playoff bracket. Apply CFP autobid rules in real time.  
**Notes:** Pairs naturally with Ideas 2, 5, and 6.

---

## Idea 4 — Following Tab (Replaces Teams + Rivalries)
**Status:** Idea  
**Type:** MAUI UX  
**Summary:**  
Toggle/dropdown: Teams | Games | Rivalries. Gold `+` above Wk on game card to follow a game. Followed games pinned at top of Following → Games above EPIC tier. Followed games tier icon = gold `+`. Tapping team/game opens expanded detail card.  
**Notes:** Strong user-retention feature.

---

## Idea 5 — Projections Tab — Playoffs View
**Status:** Idea  
**Type:** MAUI/Web UX + Projection  
**Summary:**  
Add Playoffs view alongside Standings and Title Game. Tab order: `Standings | Title Game | Playoffs`. Apply CFP autobid rules to project full bracket. Projected games show `P` badge.

---

## Idea 6 — Projections Tab — Playoff Game Projections
**Status:** Idea  
**Type:** Projection + UI  
**Summary:**  
When CFP bracket is set, add projected scores for each playoff game. Use existing GamePredictionService. Update as bracket advances.

---

## Idea 7 — Projections Tab — Sandbox
**Status:** Idea  
**Type:** UX + Projection  
**Summary:**  
Mix and match any two teams at current season state. Show projected outcome. Sandbox games can be followed with `P` badge.

---

## Idea 8 — Conference Table in DB
**Status:** Idea  
**Type:** Data Model / Architecture  
**Summary:**  
Add dedicated Conference table with FK from Team. Fields: name, abbreviation, tier, division format, membership status. Remove fragile string matching. Support historical conference switches.  
**Notes:** High-value structural fix. Enables cleaner analytics and trend graphs.

---

## Idea 9 — Global/Per-Tab Navigation Config
**Status:** Idea  
**Type:** MAUI App Architecture  
**Summary:**  
NavigationConfigService singleton stores IsWeekGlobal, IsYearGlobal. SharedNavigationState holds global year/week. Each ViewModel checks config. Config tab UI includes toggles.  
**Notes:** Good first “Config” feature and quality-of-life booster.

---

## Idea 10 — Rankings throughWeek Support
**Status:** Idea  
**Type:** API Enhancement  
**Summary:**  
Add `throughWeek` parameter to power rankings endpoint so Rankings tab shows week-specific ratings consistent with Scores and Projections.  
**Notes:** Fast win; consistency across app views.

---

## Idea 11 — Trend Graphs
**Status:** Idea  
**Type:** Analytics (Web-first)  
**Summary:**  
Win rate by conference over decades, scoring trends, power rating trajectories, upset frequency by week. Include conference-switch annotations. Query by current membership while showing full historical context.

---

## Idea 12 — Poll Data Integration
**Status:** Idea  
**Type:** Data Integration  
**Summary:**  
Integrate AP Poll and Coaches Poll alongside power ratings. Highlight model-vs-poll divergence.

---

## Idea 13 — Vegas Odds Integration
**Status:** Idea  
**Type:** Data Integration + Monetization  
**Summary:**  
Use free API for Sun/Wed updates. Store odds in Games table. Compare model projection vs Vegas line. Premium unlock candidate.

---

## Idea 14 — Freemium Monetization
**Status:** Idea  
**Type:** Product / Monetization  
**Summary:**  
Free: Scores, Rankings, Teams, Rivalries.  
Premium ($0.99): projected scores, margins, O/U, Vegas odds. Parenthetical `( )` format already built.

---

## Idea 15 — Azure Deployment + CI/CD
**Status:** Idea  
**Type:** DevOps / Platform  
**Summary:**  
API to Azure App Service. GitHub Actions CI/CD on push to main. React+TypeScript web frontend to Azure Static Web Apps. Later migrate SQLite to Azure SQL free tier.

---

## Idea 16 — Expanded Game Metrics Panel (Scores Tab)
**Status:** Idea  
**Type:** UX + Premium Surface  
**Summary:**  
Tap a game in Scores to expand panel with projected metrics (projected scores, margin, O/U, confidence, future Vegas comparison). Collapse on second tap. Free sees actual scores only; premium unlock shows projections.

---

## Idea 17 — Paywall Implementation for Projections
**Status:** Idea  
**Type:** Monetization / Platform  
**Summary:**  
Implement $0.99 unlock flow for projection features. Includes store IAP integration (App Store/Google Play), paywall gates in Scores panel, Projections tab, and future Vegas odds. Free tier shows `( )`; premium reveals values. Consider trial/preview mode.

---

## Known Bugs

### Bug A — Alternating row shading wrong when filtered
**Status:** Open  
**Fix direction:** Re-stamp `IsOddRow` based on filtered position index.

### Bug B — Data loading through AbsoluteLayout page host
**Status:** Verify next session  
**Fix direction:** Re-test navigation + load lifecycle in page host and confirm event ordering.

---

## Decision Log (Optional)
- YYYY-MM-DD: (decision)
- YYYY-MM-DD: (decision)
