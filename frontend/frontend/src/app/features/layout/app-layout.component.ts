import { Component, computed } from '@angular/core';
import { CommonModule, TitleCasePipe } from '@angular/common';
import { Router, RouterOutlet, RouterLink, RouterLinkActive } from '@angular/router';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatListModule } from '@angular/material/list';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatDividerModule } from '@angular/material/divider';
import { MatTooltipModule } from '@angular/material/tooltip';
import { AuthService } from '../../core/services/auth.service';

interface NavItem {
  path: string;
  label: string;
  icon: string;
  roles: string[];
}

@Component({
  selector: 'app-layout',
  standalone: true,
  imports: [
    CommonModule, TitleCasePipe, RouterOutlet, RouterLink, RouterLinkActive,
    MatToolbarModule, MatSidenavModule, MatListModule,
    MatIconModule, MatButtonModule, MatDividerModule, MatTooltipModule
  ],
  templateUrl: './app-layout.component.html',
  styleUrls: ['./app-layout.component.scss']
})
export class AppLayoutComponent {
  sidenavOpen = true;

  private readonly allNav: NavItem[] = [
    { path: '/app',             label: 'Painel',            icon: 'dashboard',        roles: ['aluno','preceptor','supervisor'] },
    { path: '/app/check-in',    label: 'Registrar presença', icon: 'location_on',      roles: ['aluno'] },
    { path: '/app/historico',   label: 'Meu histórico',      icon: 'assignment',       roles: ['aluno'] },
    { path: '/app/acompanhamentos', label: 'Acompanhamentos', icon: 'description',    roles: ['aluno','preceptor','supervisor'] },
    { path: '/app/preceptor',   label: 'Meus alunos',        icon: 'star',             roles: ['preceptor'] },
    { path: '/app/locais',      label: 'Locais',             icon: 'business',         roles: ['supervisor'] },
    { path: '/app/rodizios',    label: 'Rodízios',           icon: 'calendar_today',   roles: ['supervisor'] },
    { path: '/app/usuarios',    label: 'Usuários',           icon: 'people',           roles: ['supervisor'] },
    { path: '/app/relatorios',  label: 'Relatórios',         icon: 'bar_chart',        roles: ['supervisor'] }
  ];

  navItems = computed(() => {
    const role = this.auth.role();
    return this.allNav.filter(n => !role || n.roles.includes(role));
  });

  user = this.auth.user;
  role = this.auth.role;

  constructor(private auth: AuthService, private router: Router) {}

  logout(): void { this.auth.logout(); }
}
