import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatInputModule } from '@angular/material/input';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatChipsModule } from '@angular/material/chips';
import { AdminApiService } from '../../services/admin-api.service';

export interface PostseasonGame {
  id: number;
  homeTeam: string;
  awayTeam: string;
  week: number;
  gameDate: string;
  seasonType: string;
  notes?: string;
}

@Component({
  selector: 'app-postseason',
  standalone: true,
  imports: [
    CommonModule, FormsModule,
    MatCardModule, MatButtonModule, MatIconModule, MatCheckboxModule,
    MatInputModule, MatFormFieldModule, MatSnackBarModule,
    MatProgressSpinnerModule, MatProgressBarModule, MatChipsModule,
  ],
  templateUrl: './postseason.component.html',
  styleUrl: './postseason.component.scss',
})
export class PostseasonComponent implements OnInit {
  year: number = new Date().getFullYear();
  games: PostseasonGame[] = [];
  gamesByWeek: { week: number; games: PostseasonGame[] }[] = [];
  playoffIds = new Set<number>();
  loading = false;
  saving = false;
  error: string | null = null;

  constructor(private api: AdminApiService, private snack: MatSnackBar) {}

  ngOnInit() {
    this.loadGames();
  }

  loadGames() {
    this.loading = true;
    this.error = null;
    this.playoffIds.clear();

    this.api.getPostseasonGames(this.year).subscribe({
      next: (data: any) => {
        this.games = data.games ?? data;

        // Pre-check any already tagged as playoff
        this.games.forEach(g => {
          if (g.seasonType === 'playoff') this.playoffIds.add(g.id);
        });

        // Group by week
        const weekMap = new Map<number, PostseasonGame[]>();
        this.games.forEach(g => {
          if (!weekMap.has(g.week)) weekMap.set(g.week, []);
          weekMap.get(g.week)!.push(g);
        });
        this.gamesByWeek = Array.from(weekMap.entries())
          .sort((a, b) => a[0] - b[0])
          .map(([week, games]) => ({ week, games }));

        this.loading = false;
      },
      error: () => {
        this.error = 'Failed to load postseason games.';
        this.loading = false;
      },
    });
  }

  toggle(gameId: number, checked: boolean) {
    if (checked) this.playoffIds.add(gameId);
    else this.playoffIds.delete(gameId);
  }

  isChecked(gameId: number): boolean {
    return this.playoffIds.has(gameId);
  }

  save() {
    this.saving = true;
    const ids = Array.from(this.playoffIds);
    this.api.tagAsPlayoff(ids).subscribe({
      next: () => {
        this.snack.open(`${ids.length} game(s) tagged as playoff.`, 'OK', { duration: 4000 });
        this.saving = false;
        this.loadGames();
      },
      error: () => {
        this.snack.open('Save failed — check the API logs.', 'OK', { duration: 5000, panelClass: 'snack-error' });
        this.saving = false;
      },
    });
  }

  get dirtyCount(): number {
    return this.playoffIds.size;
  }
}
