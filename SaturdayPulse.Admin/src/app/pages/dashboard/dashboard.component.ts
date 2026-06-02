import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { AdminApiService } from '../../services/admin-api.service';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, MatCardModule, MatButtonModule, MatIconModule, MatProgressSpinnerModule],
  templateUrl: './dashboard.component.html',
  styleUrl: './dashboard.component.scss',
})
export class DashboardComponent implements OnInit {
  diagnostic: any = null;
  loading = false;
  error: string | null = null;

  constructor(private api: AdminApiService) {}

  ngOnInit() {
    this.loadDiagnostic();
  }

  loadDiagnostic() {
    this.loading = true;
    this.error = null;
    this.api.getDiagnostic().subscribe({
      next: data => { this.diagnostic = data; this.loading = false; },
      error: err => { this.error = 'Failed to load diagnostic data.'; this.loading = false; },
    });
  }
}
