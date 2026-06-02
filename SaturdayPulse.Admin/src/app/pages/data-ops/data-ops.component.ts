import { Component, ElementRef, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatDividerModule } from '@angular/material/divider';
import { AdminApiService } from '../../services/admin-api.service';

interface LogEntry {
  time: string;
  message: string;
  status: 'info' | 'success' | 'error' | 'running';
}

@Component({
  selector: 'app-data-ops',
  standalone: true,
  imports: [
    CommonModule, FormsModule,
    MatButtonModule, MatIconModule,
    MatInputModule, MatFormFieldModule,
    MatProgressBarModule, MatDividerModule,
  ],
  templateUrl: './data-ops.component.html',
  styleUrl: './data-ops.component.scss',
})
export class DataOpsComponent {
  @ViewChild('logPanel') logPanel!: ElementRef;

  // Weekly params
  weeklyYear: number = new Date().getFullYear();
  weeklyWeek: number = 1;

  // Season params
  seasonYear: number = new Date().getFullYear();

  running = false;
  log: LogEntry[] = [];

  constructor(private api: AdminApiService) {}

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

  async runWeeklyRefresh() {
    this.running = true;
    this.append(`── Weekly Refresh  Year=${this.weeklyYear}  Week=${this.weeklyWeek} ──`, 'info');
    try {
      await this.step('Load Games',              () => this.api.loadGames(this.weeklyYear, this.weeklyWeek));
      await this.step('Load Lines',              () => this.api.loadLines(this.weeklyYear, this.weeklyWeek));
      await this.step('Update Team Records',     () => this.api.updateTeamRecords(this.weeklyYear));
      await this.step('Update Weekly Metrics',   () => this.api.updateWeeklyMetrics(this.weeklyYear, this.weeklyWeek));
      await this.step('Compute Weekly Snapshot', () => this.api.computeWeekly(this.weeklyYear, this.weeklyWeek));
      await this.step('Rolling Averages',        () => this.api.calculateRollingAverages(this.weeklyYear, this.weeklyWeek));
      this.append(`── Complete ──`, 'success');
    } catch {
      this.append(`── Stopped due to error ──`, 'error');
    }
    this.running = false;
  }

  async runLoadLines() {
    this.running = true;
    this.append(`── Load Lines  Year=${this.weeklyYear}  Week=${this.weeklyWeek} ──`, 'info');
    try {
      await this.step('Load Lines', () => this.api.loadLines(this.weeklyYear, this.weeklyWeek));
      this.append(`── Complete ──`, 'success');
    } catch {
      this.append(`── Stopped due to error ──`, 'error');
    }
    this.running = false;
  }

  async runSeasonSetup() {
    this.running = true;
    this.append(`── Season Setup  Year=${this.seasonYear} ──`, 'info');
    try {
      await this.step('Load Conferences',         () => this.api.loadConferences());
      await this.step('Load Teams',               () => this.api.loadTeams(this.seasonYear));
      await this.step('Build Conference History', () => this.api.buildTeamsConferenceHistory(this.seasonYear));
      await this.step('Initialize Season',        () => this.api.initializeSeason(this.seasonYear));
      await this.step('Load Portal',              () => this.api.loadPortal(this.seasonYear));
      await this.step('Compute Portal Metrics',   () => this.api.computePortalMetrics(this.seasonYear));
      this.append(`── Complete ──`, 'success');
    } catch {
      this.append(`── Stopped due to error ──`, 'error');
    }
    this.running = false;
  }

  clearLog() { this.log = []; }
}
