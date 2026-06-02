# SaturdayPulse.Admin — Scaffold

## Files to copy into your project

Copy each file from this package into the matching path under your
`SaturdayPulse.Admin/` folder, replacing any existing file with the same name.

```
src/app/app.component.ts
src/app/app.config.ts
src/app/app.routes.ts
src/app/shell/shell.component.ts
src/app/shell/shell.component.html
src/app/shell/shell.component.scss
src/app/services/admin-api.service.ts
src/app/pages/dashboard/dashboard.component.ts
src/app/pages/dashboard/dashboard.component.html
src/app/pages/dashboard/dashboard.component.scss
src/app/pages/weekly-ops/weekly-ops.component.ts
src/app/pages/weekly-ops/weekly-ops.component.html
src/app/pages/weekly-ops/weekly-ops.component.scss
src/app/pages/postseason/postseason.component.ts
src/app/pages/postseason/postseason.component.html
src/app/pages/postseason/postseason.component.scss
src/app/pages/season-setup/season-setup.component.ts
src/app/pages/season-setup/season-setup.component.html
src/app/pages/season-setup/season-setup.component.scss
src/app/pages/metrics-rebuild/metrics-rebuild.component.ts
src/app/pages/metrics-rebuild/metrics-rebuild.component.html
src/app/pages/metrics-rebuild/metrics-rebuild.component.scss
src/app/pages/analytics/analytics.component.ts
src/app/pages/analytics/analytics.component.html
src/app/pages/analytics/analytics.component.scss
```

## Create missing folders first

The `ng new` scaffold won't have these folders yet — create them before copying:

```
src/app/shell/
src/app/services/
src/app/pages/dashboard/
src/app/pages/weekly-ops/
src/app/pages/postseason/
src/app/pages/season-setup/
src/app/pages/metrics-rebuild/
src/app/pages/analytics/
```

## Set your API base URL

Open `src/app/services/admin-api.service.ts` and confirm:

```typescript
private base = 'http://localhost:5000/api';
```

Change the port to match wherever your SaturdayPulse.Api runs locally.

## Two backend endpoints still needed

The postseason tagging page calls two endpoints that don't exist yet in DeveloperController:

- `POST api/developer/tagAsPlayoff`   — body: `{ gameIds: number[] }`
- `POST api/developer/untagAsPlayoff` — body: `{ gameIds: number[] }`

Add these before testing the Postseason page.

## After copying, run

```
ng serve
```

Navigate to http://localhost:4200 — you should see the full shell with
side navigation and all six pages routing correctly.
