import { Component } from '@angular/core';
import { RouterOutlet, RouterLink, RouterLinkActive } from '@angular/router';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatListModule } from '@angular/material/list';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';

interface NavItem {
  label: string;
  icon: string;
  route: string;
}

@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [
    RouterOutlet,
    RouterLink,
    RouterLinkActive,
    MatSidenavModule,
    MatToolbarModule,
    MatListModule,
    MatIconModule,
    MatButtonModule,
  ],
  templateUrl: './shell.component.html',
  styleUrl: './shell.component.scss',
})
export class ShellComponent {
  navItems: NavItem[] = [
    { label: 'Dashboard',        icon: 'dashboard',      route: '/dashboard'       },
    { label: 'Data Operations', icon: 'storage', route: '/data-ops' },
    { label: 'Postseason',       icon: 'emoji_events',   route: '/postseason'      },
    { label: 'Metrics Rebuild',  icon: 'build',          route: '/metrics-rebuild' },
    { label: 'Analytics',        icon: 'bar_chart',      route: '/analytics'       },
  ];
}
