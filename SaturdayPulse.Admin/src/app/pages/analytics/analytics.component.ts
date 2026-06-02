import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTableModule } from '@angular/material/table';
import { AdminApiService } from '../../services/admin-api.service';

@Component({
  selector: 'app-analytics',
  standalone: true,
  imports: [
    CommonModule, FormsModule,
    MatCardModule, MatButtonModule, MatIconModule,
    MatInputModule, MatFormFieldModule,
    MatProgressSpinnerModule, MatTableModule,
  ],
  templateUrl: './analytics.component.html',
  styleUrl: './analytics.component.scss',
})
export class AnalyticsComponent {
  startYear: number = 2015;
  endYear: number = new Date().getFullYear();
  projectionAccuracy: any = null;
  analytics: any = null;
  loading: { [key: string]: boolean } = {};

  constructor(private api: AdminApiService) {}

  loadProjectionAccuracy() {
    this.loading['proj'] = true;
    this.api.getProjectionAccuracy(this.startYear, this.endYear).subscribe({
      next: data => { this.projectionAccuracy = data; this.loading['proj'] = false; },
      error: () => { this.loading['proj'] = false; },
    });
  }

  loadAnalytics() {
    this.loading['analytics'] = true;
    this.api.getAnalytics(this.startYear, this.endYear).subscribe({
      next: data => { this.analytics = data; this.loading['analytics'] = false; },
      error: () => { this.loading['analytics'] = false; },
    });
  }
}
