# SaturdayPulse.AdminBlazor

Replaces `SaturdayPulse.Admin` (Angular) with a Blazor Web App. Angular
project is left untouched for reference — delete it once you've verified
parity here.

## Setup

1. Copy this whole `SaturdayPulse.AdminBlazor/` folder into your solution
   folder, next to `SaturdayPulse.Api` and `SaturdayPulse.Mobile`.
2. Also copy `SaturdayPulse.Core/` in as a sibling folder at the same level
   (new shared-contracts project — `ApplicationContentDocument`/
   `ContentSection` now live there, referenced via `<ProjectReference>`
   instead of a local copy).
3. Add both to the `.sln` (Visual Studio: right-click solution → Add →
   Existing Project → select each `.csproj`).
4. Restore NuGet packages (pulls in MudBlazor, pinned to 7.15.0 — let
   NuGet bump it to the current 7.x/8.x release if you want).
5. `appsettings.Development.json` points at `https://localhost:7010/api`
   to match `SaturdayPulse.Api`'s HTTPS launch profile. `appsettings.json`
   has a placeholder for the Azure URL — fill that in before your first
   publish.
6. Two backend endpoints the Postseason page needs (`POST
   developer/tagAsPlayoff` / `untagAsPlayoff`) — confirmed already
   present in `DeveloperController.cs`, nothing to add.
7. F5 / `dotnet run` — opens to the Dashboard.

## Status: full parity, all 5 pages built

| Page | Status |
|---|---|
| Dashboard | Diagnostic stat cards |
| Data Operations | Weekly Refresh, Load Lines, Season Setup |
| Postseason | Load/tag/save playoff games workflow |
| Metrics Rebuild | Tiered ops, dependency cascade, tier-year cascade |
| Analytics | Projection + Portal accuracy, glossary sidebar |

`Services/AdminApiService.cs` is a complete 1:1 translation of
`admin-api.service.ts`. Endpoints consumed by a built page return real
DTOs (`DiagnosticDto`, `ProjectionAccuracyResultDto`,
`PortalAccuracyResultDto`, `PostseasonGameDto`, etc.) — the Analytics
ones were matched directly against the actual C# response records in
`SaturdayPulse.Api/Contracts/Responses/GameProjectionAccuracyDTOs.cs`
found in the API project, not guessed from the Angular `any` types.
The one endpoint no page calls (`developer/analytics`) is still
untyped `JsonElement`.

## Shared components (factored out, not copy-pasted 3-6x like the Angular original)

- **`ActivityLogPanel`** — the log-sidebar/step-runner UI used by Data
  Operations and Metrics Rebuild (Postseason has its own single-column
  variant since its layout doesn't match the sidebar shape).
- **`CollapsibleSection`** — the repeated collapsed-by-default
  header+table pattern in Analytics (By Week, By Year, By Conference,
  By Phase, By Portal Group).
- **`StatCard`** — the 6x-repeated dashboard card markup.
- **`GlossaryEntry`** — the 18x-repeated term/definition pattern in the
  Analytics glossary sidebar.
- **`AdminApiException`** — carries the API's `message` field through
  to the UI, replacing the Angular pattern of `err?.error?.message ??
  'check API logs'` repeated in every step-runner.

All shared visual styling (log panel, result cards, data tables,
glossary, tier/op rows) lives in one global `wwwroot/css/admin.css`
rather than per-page scoped CSS — Blazor's CSS isolation doesn't reach
into child components without deliberate `::deep` wiring everywhere,
and there's enough cross-page shared vocabulary here that one
stylesheet is simpler and less error-prone than juggling that per page.

## Notable decisions (flag if you want any of these changed)

- **Hosting model:** Blazor Web App template, global Interactive Server
  render mode. One deployable ASP.NET Core app, same story as the API.
- **UI library:** MudBlazor, swapped in for Angular Material 1:1.
- **API base URL:** moved from a hardcoded one-line toggle in the
  Angular service into `appsettings.json` / `appsettings.Development.json`.
- **Nav drawer:** `DrawerVariant.Responsive` so it collapses behind a
  hamburger on phone-width screens (the Angular sidenav was always
  pinned open, which doesn't work on a phone).
- **Postseason `refreshCounts` behavior preserved as-is**: after a
  checkbox toggle, "Bowl" count = currently-unchecked games and
  "Playoff" count = currently-checked games (not a re-check of each
  game's actual `seasonType`). That's what the Angular original does —
  kept it exactly rather than "fixing" it, since this is a port, not a
  redesign.

## Not yet built (wasn't in the Angular reference either)

- Season Pass beta-grant admin UI (folds into this console per the
  priority list, but is new functionality, not a port of anything).
- Shared class library for cross-project config (Discord webhook URL,
  etc.) — separate project, not part of this one.

## Next up

Let me know if you want a pass on visual polish/pixel-parity with the
Angular scss (colors, spacing were approximated against MudBlazor's
theme variables rather than matched exactly), or if you want to move
straight to the Season Pass grant UI or the calc-engine refactor.
