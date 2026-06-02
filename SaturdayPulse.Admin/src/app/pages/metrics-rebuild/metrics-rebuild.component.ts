import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatDialogModule, MatDialog } from '@angular/material/dialog';
import { AdminApiService } from '../../services/admin-api.service';

@Component({
  selector: 'app-metrics-rebuild',
  standalone: true,
  imports: [
    CommonModule, FormsModule,
    MatCardModule, MatButtonModule, MatIconModule,
    MatInputModule, MatFormFieldModule, MatSnackBarModule,
    MatProgressBarModule, MatDialogModule,
  ],
  templateUrl: './metrics-rebuild.component.html',
  styleUrl: './metrics-rebuild.component.scss',
})
export class MetricsRebuildComponent {
  startYear: number = 1965;
  busy: { [key: string]: boolean } = {};

  constructor(private api: AdminApiService, private snack: MatSnackBar) {}

  run(key: string, call: () => any, longRunning = false) {
    if (longRunning) {
      const confirmed = confirm('This is a long-running operation and may take several minutes. Continue?');
      if (!confirmed) return;
    }
    this.busy[key] = true;
    call().subscribe({
      next: (res: any) => {
        this.snack.open(res.message ?? 'Done', 'OK', { duration: 6000 });
        this.busy[key] = false;
      },
      error: () => {
        this.snack.open('Operation failed — check the API logs.', 'OK', { duration: 5000, panelClass: 'snack-error' });
        this.busy[key] = false;
      },
    });
  }

  backfillAllMetrics()          { this.run('allmetrics',   () => this.api.backfillAllMetrics(this.startYear), true); }
  backfillRollingAverages()     { this.run('rolling',      () => this.api.backfillRollingAverages(this.startYear), true); }
  backfillWeeklyRankings()      { this.run('weekly',       () => this.api.backfillWeeklyRankings(this.startYear), true); }
  backfillProjections()         { this.run('projections',  () => this.api.backfillProjections(this.startYear), true); }
  buildAvgScoreDifferentials()  { this.run('scorediff',    () => this.api.buildAvgScoreDifferentials(this.startYear), true); }
  recalculateScoreDeltas()      { this.run('scoredelta',   () => this.api.recalculateScoreDeltas()); }
  calculateMatchupHistories()   { this.run('matchup',      () => this.api.calculateMatchupHistories()); }
}
