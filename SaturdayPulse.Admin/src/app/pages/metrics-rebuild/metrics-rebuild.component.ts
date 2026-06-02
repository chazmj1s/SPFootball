import { Component, ElementRef, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatDividerModule } from '@angular/material/divider';
import { MatTooltipModule } from '@angular/material/tooltip';
import { AdminApiService } from '../../services/admin-api.service';

interface LogEntry {
  time: string;
  message: string;
  status: 'info' | 'success' | 'error' | 'running';
}

export interface OpParams {
  year?: number;
  week?: number;
}

export interface Operation {
  key: string;
  label: string;
  estimateMinutes: number;
  selected: boolean;
  autoSelected: boolean;
  dependencies: string[];
  paramTypes: ('year' | 'week')[];
  yearRequired: boolean;
  weekRequired: boolean;
  params: OpParams;
  defaultYear: number | undefined;
  defaultWeek: number | undefined;
  call: (p: OpParams) => any;
}

export interface Tier {
  key: string;
  label: string;
  collapsed: boolean;
  tierYear?: number;
  ops: Operation[];
}

@Component({
  selector: 'app-metrics-rebuild',
  standalone: true,
  imports: [
    CommonModule, FormsModule,
    MatButtonModule, MatIconModule, MatCheckboxModule,
    MatProgressBarModule, MatDividerModule, MatTooltipModule,
  ],
  templateUrl: './metrics-rebuild.component.html',
  styleUrl: './metrics-rebuild.component.scss',
})
export class MetricsRebuildComponent {
  @ViewChild('logPanel') logPanel!: ElementRef;

  readonly currentYear = new Date().getFullYear();
  readonly years: number[] = Array.from({ length: this.currentYear - 1964 }, (_, i) => this.currentYear - i);
  readonly weeks: number[] = Array.from({ length: 20 }, (_, i) => i + 1);

  running = false;
  log: LogEntry[] = [];
  tiers: Tier[] = [];

  constructor(private api: AdminApiService) {
    this.initTiers();
  }

  private op(
    key: string, label: string, estimateMinutes: number,
    dependencies: string[], paramTypes: ('year' | 'week')[],
    yearRequired: boolean, weekRequired: boolean,
    defaultYear: number | undefined, defaultWeek: number | undefined,
    call: (p: OpParams) => any
  ): Operation {
    return {
      key, label, estimateMinutes, selected: false, autoSelected: false,
      dependencies, paramTypes, yearRequired, weekRequired,
      defaultYear, defaultWeek,
      params: { year: defaultYear, week: defaultWeek },
      call,
    };
  }

  private initTiers() {
    const y = this.currentYear;
    this.tiers = [
      {
        key: 'data', label: 'Tier 1 — Data Load', collapsed: false, tierYear: undefined,
        ops: [
          this.op('conferences', 'Load Conferences',  0.1, [],              [],               false, false, undefined, undefined, (_p) => this.api.loadConferences()),
          this.op('teams',       'Load Teams (Bulk)', 0.1, [],              ['year'],          true,  false, y,         undefined, (p)  => this.api.loadTeamsBulk(p.year!)),
          this.op('games',       'Load Games (Bulk)', 3,   [],              ['year'],          true,  false, y,         undefined, (p)  => this.api.loadGamesBulk(p.year!)),
          this.op('lines',       'Load Lines (Bulk)', 3,   [],              ['year'],          true,  false, y,         undefined, (p)  => this.api.loadLinesBulk(p.year!)),
        ],
      },
      {
        key: 'metrics', label: 'Tier 2 — Derived Records', collapsed: false, tierYear: undefined,
        ops: [
          this.op('records',        'Team Records',    2,  [],               ['year'],          false, false, y, undefined, (p) => this.api.updateTeamRecords(p.year)),
          this.op('sos',            'SOS',             5,  ['records'],      ['year', 'week'],  false, false, y, undefined, (p) => this.api.setSOS(p.year, p.week)),
          this.op('powerratings',   'Power Ratings',   5,  ['sos'],          ['year'],          false, false, y, undefined, (p) => this.api.calculatePowerRatings(p.year)),
          this.op('rankings',       'Rankings',        10, ['powerratings'], ['year'],          false, false, y, undefined, (p) => this.api.calculateRankings(p.year)),
          this.op('weeklyrankings', 'Weekly Rankings', 40, ['rankings'],     ['year'],          false, false, y, undefined, (p) => this.api.backfillWeeklyRankings(p.year)),
        ],
      },
      {
        key: 'analytics', label: 'Tier 3 — Analytics', collapsed: false, tierYear: undefined,
        ops: [
          this.op('rolling',     'Rolling Averages',    8,  ['weeklyrankings'], ['year'], false, false, y, undefined, (p) => this.api.backfillRollingAverages(p.year)),
          this.op('projections', 'Projections',         40, ['weeklyrankings'], ['year'], false, false, y, undefined, (p) => this.api.backfillProjections(p.year)),
          this.op('scorediffs',  'Score Differentials', 3,  [],                 ['year'], false, false, y, undefined, (p) => this.api.buildAvgScoreDifferentials(p.year)),
          this.op('matchups',    'Matchup Histories',   2,  [],                 [],       false, false, undefined, undefined, (_p) => this.api.calculateMatchupHistories()),
        ],
      },
    ];
  }

  // ── Tier year cascade ──────────────────────────────────────────
  onTierYearChange(tier: Tier, year: number | undefined) {
    tier.tierYear = year;
    if (year !== undefined) {
      // Cascade to all children that have a year param
      tier.ops.forEach(op => {
        if (op.paramTypes.includes('year')) op.params.year = year;
        // SOS week resets to '-' when parent overrides year
        if (op.key === 'sos') op.params.week = undefined;
      });
    }
    // On reset to '-' children keep their current values — no cascade back
  }

  onChildYearChange(tier: Tier, op: Operation, year: number | undefined) {
    op.params.year = year;
    // If any child diverges from the tier year, reset the tier header to '-'
    const allMatch = tier.ops
      .filter(o => o.paramTypes.includes('year'))
      .every(o => o.params.year === tier.tierYear);
    if (!allMatch) tier.tierYear = undefined;
  }

  // ── Checkbox logic ─────────────────────────────────────────────
  private allOps(): Operation[] { return this.tiers.flatMap(t => t.ops); }

  private findOp(key: string): Operation | undefined {
    return this.allOps().find(o => o.key === key);
  }

  private resolveDependencies(op: Operation, checking: boolean) {
    if (checking) {
      op.dependencies.forEach(depKey => {
        const dep = this.findOp(depKey);
        if (dep && !dep.selected) {
          dep.selected = true;
          dep.autoSelected = true;
          this.resolveDependencies(dep, true);
        }
      });
    } else {
      op.autoSelected = false;
      this.allOps().forEach(o => {
        if (o.autoSelected) {
          const stillNeeded = this.allOps().some(x => x.selected && x.dependencies.includes(o.key));
          if (!stillNeeded) { o.selected = false; o.autoSelected = false; }
        }
      });
    }
  }

  onToggle(op: Operation, checked: boolean) {
    op.selected = checked;
    if (!checked) op.autoSelected = false;
    this.resolveDependencies(op, checked);
  }

  isTierIndeterminate(tier: Tier): boolean {
    const sel = tier.ops.filter(o => o.selected).length;
    return sel > 0 && sel < tier.ops.length;
  }

  isTierChecked(tier: Tier): boolean { return tier.ops.every(o => o.selected); }

  onTierToggle(tier: Tier, checked: boolean) {
    tier.ops.forEach(op => { op.selected = checked; op.autoSelected = false; });
    if (checked) tier.ops.forEach(op => this.resolveDependencies(op, true));
  }

  selectAll() { this.tiers.forEach(t => this.onTierToggle(t, true)); }
  clearAll()  { this.tiers.forEach(t => t.ops.forEach(o => { o.selected = false; o.autoSelected = false; })); }

  toggleTier(tier: Tier) { tier.collapsed = !tier.collapsed; }
  tierSelectedCount(tier: Tier): number { return tier.ops.filter(o => o.selected).length; }

  get selectedOps(): Operation[] { return this.allOps().filter(o => o.selected); }

  get totalEstimateMinutes(): number { return this.selectedOps.reduce((sum, o) => sum + o.estimateMinutes, 0); }

  get estimateLabel(): string {
    const m = this.totalEstimateMinutes;
    if (m === 0) return '';
    if (m < 60) return `~${Math.round(m)} min`;
    const h = Math.floor(m / 60);
    const rem = Math.round(m % 60);
    return rem > 0 ? `~${h}h ${rem}m` : `~${h}h`;
  }

  private timestamp(): string { return new Date().toLocaleTimeString('en-US', { hour12: false }); }

  private append(message: string, status: LogEntry['status'] = 'info') {
    this.log.push({ time: this.timestamp(), message, status });
    setTimeout(() => {
      if (this.logPanel) this.logPanel.nativeElement.scrollTop = this.logPanel.nativeElement.scrollHeight;
    }, 0);
  }

  private step(op: Operation): Promise<any> {
    const start = Date.now();
    const paramStr = [
      op.params.year != null ? `year=${op.params.year}` : null,
      op.params.week != null && op.paramTypes.includes('week') ? `week=${op.params.week}` : null,
    ].filter(Boolean).join(' ');

    return new Promise((resolve, reject) => {
      this.append(`Starting ${op.label}${paramStr ? ' (' + paramStr + ')' : ''}...`, 'running');
      op.call(op.params).subscribe({
        next: (res: any) => {
          const elapsed = ((Date.now() - start) / 1000 / 60).toFixed(1);
          this.append(`✓ ${res.message ?? op.label + ' complete'} (${elapsed}m)`, 'success');
          resolve(res);
        },
        error: (err: any) => {
          this.append(`✗ ${op.label} failed — ${err?.error?.message ?? 'check API logs'}`, 'error');
          reject(err);
        },
      });
    });
  }

  async runSelected() {
    if (this.selectedOps.length === 0) return;
    this.running = true;
    this.append(`── Rebuild  ${this.estimateLabel} ──`, 'info');
    try {
      for (const op of this.selectedOps) await this.step(op);
      this.append(`── Complete ──`, 'success');
    } catch {
      this.append(`── Stopped due to error ──`, 'error');
    }
    this.running = false;
  }

  clearLog() { this.log = []; }
}
