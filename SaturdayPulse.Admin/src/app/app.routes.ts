import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', redirectTo: 'dashboard', pathMatch: 'full' },
  {
    path: 'dashboard',
    loadComponent: () =>
      import('./pages/dashboard/dashboard.component').then(m => m.DashboardComponent),
  },
  {
    path: 'data-ops',
    loadComponent: () =>
      import('./pages/data-ops/data-ops.component').then(m => m.DataOpsComponent),
  },
  {
    path: 'postseason',
    loadComponent: () =>
      import('./pages/postseason/postseason.component').then(m => m.PostseasonComponent),
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
