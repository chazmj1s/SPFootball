import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', redirectTo: 'dashboard', pathMatch: 'full' },
  {
    path: 'dashboard',
    loadComponent: () =>
      import('./pages/dashboard/dashboard.component').then(m => m.DashboardComponent),
  },
  {
    path: 'weekly-ops',
    loadComponent: () =>
      import('./pages/weekly-ops/weekly-ops.component').then(m => m.WeeklyOpsComponent),
  },
  {
    path: 'postseason',
    loadComponent: () =>
      import('./pages/postseason/postseason.component').then(m => m.PostseasonComponent),
  },
  {
    path: 'season-setup',
    loadComponent: () =>
      import('./pages/season-setup/season-setup.component').then(m => m.SeasonSetupComponent),
  },
  {
    path: 'metrics-rebuild',
    loadComponent: () =>
      import('./pages/metrics-rebuild/metrics-rebuild.component').then(m => m.MetricsRebuildComponent),
  },
  {
    path: 'analytics',
    loadComponent: () =>
      import('./pages/analytics/analytics.component').then(m => m.AnalyticsComponent),
  },
  { path: '**', redirectTo: 'dashboard' },
];
