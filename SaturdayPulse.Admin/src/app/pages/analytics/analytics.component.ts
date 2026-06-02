import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatDividerModule } from '@angular/material/divider';
import { AdminApiService } from '../../services/admin-api.service';

@Component({
  selector: 'app-analytics',
  standalone: true,
  imports: [
    CommonModule, FormsModule,
    MatButtonModule, MatIconModule,
    MatProgressBarModule, MatDividerModule,
  ],
  templateUrl: './analytics.component.html',
  styleUrl: './analytics.component.scss',
})
export class AnalyticsComponent {
  readonly currentYear = new Date().getFullYear();
  readonly years: number[] = Array.from({ length: this.currentYear - 1964 }, (_, i) => this.currentYear - i);

  // Projection Accuracy
  projStartYear: number | undefined = 2015;
  projEndYear: number | undefined = this.currentYear;
  projData: any = null;
  projLoading = false;

  // Portal Accuracy
  portalStartYear: number | undefined = 2021;
  portalEndYear: number | undefined = this.currentYear;
  portalData: any = null;
  portalLoading = false;

  // Collapsible sections — all start collapsed
  collapsedSections = new Set<string>([
    'proj-byYear', 'proj-byWeek', 'proj-byConference', 'proj-byPhase', 'portal-byGroup'
  ]);

  constructor(private api: AdminApiService) {}

  toggleSection(key: string) {
    if (this.collapsedSections.has(key)) this.collapsedSections.delete(key);
    else this.collapsedSections.add(key);
  }

  isCollapsed(key: string): boolean {
    return this.collapsedSections.has(key);
  }

  loadProjectionAccuracy() {
    this.projLoading = true;
    this.projData = null;
    this.api.getProjectionAccuracy(this.projStartYear, this.projEndYear).subscribe({
      next: d => { this.projData = d; this.projLoading = false; },
      error: () => this.projLoading = false,
    });
  }

  loadPortalAccuracy() {
    this.portalLoading = true;
    this.portalData = null;
    this.api.getPortalAccuracy(this.portalStartYear, this.portalEndYear).subscribe({
      next: d => { this.portalData = d; this.portalLoading = false; },
      error: () => this.portalLoading = false,
    });
  }

  // Portal winner vs loser rows for a given period
  portalRow(group: string, period: string): any {
    return this.portalData?.byPortalGroup?.find(
      (r: any) => r.portalGroup === group && r.period === period
    );
  }
}
