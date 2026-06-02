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
import { AdminApiService } from '../../services/admin-api.service';

@Component({
  selector: 'app-weekly-ops',
  standalone: true,
  imports: [
    CommonModule, FormsModule,
    MatCardModule, MatButtonModule, MatIconModule,
    MatInputModule, MatFormFieldModule, MatSnackBarModule, MatProgressBarModule,
  ],
  templateUrl: './weekly-ops.component.html',
  styleUrl: './weekly-ops.component.scss',
})
export class WeeklyOpsComponent {
  year: number = new Date().getFullYear();
  week: number = 1;
  busy: { [key: string]: boolean } = {};

  constructor(private api: AdminApiService, private snack: MatSnackBar) {}

  run(key: string, call: () => any) {
    this.busy[key] = true;
    call().subscribe({
      next: (res: any) => {
        this.snack.open(res.message ?? 'Done', 'OK', { duration: 4000 });
        this.busy[key] = false;
      },
      error: () => {
        this.snack.open('Operation failed — check the API logs.', 'OK', { duration: 5000, panelClass: 'snack-error' });
        this.busy[key] = false;
      },
    });
  }

  weeklyRefresh()        { this.run('refresh',    () => this.api.weeklyRefresh(this.year, this.week)); }
  updateTeamRecords()    { this.run('records',    () => this.api.updateTeamRecords(this.year)); }
  updateWeeklyMetrics()  { this.run('metrics',    () => this.api.updateWeeklyMetrics(this.year, this.week)); }
  computeWeekly()        { this.run('compute',    () => this.api.computeWeekly(this.year, this.week)); }
  calcRollingAverages()  { this.run('rolling',    () => this.api.calculateRollingAverages(this.year, this.week)); }
  assignPostseasonWeeks(){ this.run('postseason', () => this.api.assignPostseasonWeeks(this.year)); }
}
