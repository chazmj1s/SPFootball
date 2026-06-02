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
  selector: 'app-season-setup',
  standalone: true,
  imports: [
    CommonModule, FormsModule,
    MatCardModule, MatButtonModule, MatIconModule,
    MatInputModule, MatFormFieldModule, MatSnackBarModule, MatProgressBarModule,
  ],
  templateUrl: './season-setup.component.html',
  styleUrl: './season-setup.component.scss',
})
export class SeasonSetupComponent {
  year: number = new Date().getFullYear();
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

  initializeSeason()         { this.run('init',       () => this.api.initializeSeason(this.year)); }
  loadConferences()          { this.run('conf',       () => this.api.loadConferences()); }
  loadTeams()                { this.run('teams',      () => this.api.loadTeams(this.year)); }
  buildConferenceHistory()   { this.run('confhist',   () => this.api.buildTeamsConferenceHistory(this.year)); }
  loadPortal()               { this.run('portal',     () => this.api.loadPortal(this.year)); }
  computePortalMetrics()     { this.run('portalmet',  () => this.api.computePortalMetrics(this.year)); }
}
