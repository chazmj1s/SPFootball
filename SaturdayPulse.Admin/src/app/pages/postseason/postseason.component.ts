import { Component, ElementRef, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatInputModule } from '@angular/material/input';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { AdminApiService } from '../../services/admin-api.service';

interface LogEntry {
  time: string;
  message: string;
  status: 'info' | 'success' | 'error' | 'running';
}

export interface PostseasonGame {
  id: number;
  homeName: string;
  awayName: string;
  week: number;
  gameDate: string;
  gameDay: string;
  seasonType: string;
}

export interface WeekGroup {
  week: number;
  games: PostseasonGame[];
  bowlCount: number;
  playoffCount: number;
}

@Component({
  selector: 'app-postseason',
  standalone: true,
  imports: [
    CommonModule, FormsModule,
    MatButtonModule, MatIconModule, MatCheckboxModule,
    MatInputModule, MatFormFieldModule, MatSnackBarModule,
    MatProgressBarModule,
  ],
  templateUrl: './postseason.component.html',
  styleUrl: './postseason.component.scss',
})
export class PostseasonComponent {
  @ViewChild('logPanel') logPanel!: ElementRef;

  year: number = new Date().getFullYear();
  games: PostseasonGame[] = [];
  gamesByWeek: WeekGroup[] = [];
  playoffIds = new Set<number>();
  collapsedWeeks = new Set<number>();
  running = false;
  saving = false;
  log: LogEntry[] = [];

  constructor(private api: AdminApiService, private snack: MatSnackBar) {}

  private timestamp(): string {
    return new Date().toLocaleTimeString('en-US', { hour12: false });
  }

  private append(message: string, status: LogEntry['status'] = 'info') {
    this.log.push({ time: this.timestamp(), message, status });
    setTimeout(() => {
      if (this.logPanel) {
        this.logPanel.nativeElement.scrollTop = this.logPanel.nativeElement.scrollHeight;
      }
    }, 0);
  }

  private step(label: string, call: () => any): Promise<any> {
    return new Promise((resolve, reject) => {
      this.append(`Starting ${label}...`, 'running');
      call().subscribe({
        next: (res: any) => {
          this.append(`✓ ${res.message ?? label + ' complete'}`, 'success');
          resolve(res);
        },
        error: (err: any) => {
          this.append(`✗ ${label} failed — ${err?.error?.message ?? 'check API logs'}`, 'error');
          reject(err);
        },
      });
    });
  }

  private buildWeekGroups(games: PostseasonGame[]): WeekGroup[] {
    const weekMap = new Map<number, PostseasonGame[]>();
    games.forEach(g => {
      if (!weekMap.has(g.week)) weekMap.set(g.week, []);
      weekMap.get(g.week)!.push(g);
    });
    return Array.from(weekMap.entries())
      .sort((a, b) => a[0] - b[0])
      .map(([week, games]) => ({
        week,
        games,
        bowlCount: games.filter(g => g.seasonType === 'postseason').length,
        playoffCount: games.filter(g => g.seasonType === 'playoff').length,
      }));
  }

  // Recompute counts after toggle without re-fetching
  private refreshCounts() {
    this.gamesByWeek = this.gamesByWeek.map(group => ({
      ...group,
      bowlCount: group.games.filter(g => !this.isChecked(g.id) ).length,
      playoffCount: group.games.filter(g => this.isChecked(g.id)).length,
    }));
  }

  async loadPostseason() {
    this.running = true;
    this.log = [];
    this.games = [];
    this.gamesByWeek = [];
    this.playoffIds.clear();
    this.collapsedWeeks.clear();
    this.append(`── Load Postseason  Year=${this.year} ──`, 'info');

    try {
      await this.step('Load Postseason Games', () => this.api.loadPostseasonGames(this.year));
      await this.step('Assign Postseason Weeks', () => this.api.assignPostseasonWeeks(this.year));
      this.append(`── Complete — loading game list ──`, 'success');
      this.fetchGames();
    } catch {
      this.append(`── Stopped due to error ──`, 'error');
      this.running = false;
    }
  }

  fetchGames() {
    this.api.getPostseasonGames(this.year).subscribe({
      next: (data: any) => {
        this.games = data.games ?? data;
        this.playoffIds.clear();
        this.games.forEach(g => {
          if (g.seasonType === 'playoff') this.playoffIds.add(g.id);
        });
        this.gamesByWeek = this.buildWeekGroups(this.games);
        this.running = false;
      },
      error: () => {
        this.append('✗ Failed to load game list', 'error');
        this.running = false;
      },
    });
  }

  toggle(gameId: number, checked: boolean) {
    if (checked) this.playoffIds.add(gameId);
    else this.playoffIds.delete(gameId);
    this.refreshCounts();
  }

  isChecked(gameId: number): boolean {
    return this.playoffIds.has(gameId);
  }

  toggleWeek(week: number) {
    if (this.collapsedWeeks.has(week)) this.collapsedWeeks.delete(week);
    else this.collapsedWeeks.add(week);
  }

  isCollapsed(week: number): boolean {
    return this.collapsedWeeks.has(week);
  }

  save() {
    this.saving = true;
    const ids = Array.from(this.playoffIds);
    this.api.tagAsPlayoff(ids).subscribe({
      next: () => {
        this.snack.open(`${ids.length} game(s) tagged as playoff.`, 'OK', { duration: 4000 });
        this.saving = false;
        this.fetchGames();
      },
      error: () => {
        this.snack.open('Save failed — check the API logs.', 'OK', { duration: 5000 });
        this.saving = false;
      },
    });
  }

  clearLog() { this.log = []; }

  get dirtyCount(): number { return this.playoffIds.size; }
}
