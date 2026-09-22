import { Component, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterOutlet, RouterLink, RouterLinkActive } from '@angular/router';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatListModule } from '@angular/material/list';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatDividerModule } from '@angular/material/divider';
import { MatTooltipModule } from '@angular/material/tooltip';
import { AuthService } from '../../core/services/auth.service';
import { rotuloTurma, turnoDaTurma } from '../../core/utils/turma';

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
    CommonModule, RouterOutlet, RouterLink, RouterLinkActive,
    MatToolbarModule, MatSidenavModule, MatListModule,
    MatIconModule, MatButtonModule, MatDividerModule, MatTooltipModule
  ],
  templateUrl: './app-layout.component.html',
  styleUrls: ['./app-layout.component.scss']
})
export class AppLayoutComponent {
  sidenavOpen = true;

  private readonly allNav: NavItem[] = [
    { path: '/app',             label: 'Painel',            icon: 'dashboard',        roles: ['aluno','preceptor','supervisor','coordenadora'] },
    { path: '/app/check-in',    label: 'Registrar presença', icon: 'location_on',      roles: ['aluno'] },
    { path: '/app/historico',   label: 'Meu histórico',      icon: 'assignment',       roles: ['aluno'] },
    { path: '/app/irregularidades', label: 'Irregularidades', icon: 'fact_check',      roles: ['aluno','preceptor','supervisor','coordenadora'] },
    { path: '/app/certificados', label: 'Certificados',      icon: 'workspace_premium', roles: ['aluno','supervisor','coordenadora'] },
    { path: '/app/acompanhamentos', label: 'Acompanhamentos', icon: 'description',    roles: ['aluno','preceptor','supervisor','coordenadora'] },
    { path: '/app/preceptor',   label: 'Meus alunos',        icon: 'star',             roles: ['preceptor'] },
    // "Locais" saiu do menu: era a mesma tabela de "Unidades de saúde", em outra
    // tela. A rota antiga continua existindo e redireciona para cá.
    { path: '/app/unidades',    label: 'Unidades de saúde',  icon: 'domain',           roles: ['aluno','preceptor','supervisor','coordenadora'] },
    { path: '/app/alocacoes',   label: 'Alocações',          icon: 'assignment_ind',   roles: ['supervisor','coordenadora'] },
    { path: '/app/rodizios',    label: 'Rodízios',           icon: 'calendar_today',   roles: ['supervisor','coordenadora'] },
    { path: '/app/atividades-remotas', label: 'Atividades remotas', icon: 'home_work',  roles: ['aluno','preceptor','supervisor','coordenadora'] },
    { path: '/app/excecoes',    label: 'Calendário', icon: 'event_busy',   roles: ['supervisor','coordenadora'] },
    { path: '/app/usuarios',    label: 'Usuários',           icon: 'people',           roles: ['supervisor','coordenadora'] },
    { path: '/app/relatorios',  label: 'Relatórios',         icon: 'bar_chart',        roles: ['supervisor','coordenadora'] }
  ];

  /** Rótulos de perfil: o identificador técnico não é o nome usado na faculdade. */
  private readonly rotulosPerfil: Record<string, string> = {
    aluno: 'Aluno(a)',
    preceptor: 'Preceptor(a)',
    supervisor: 'Professor(a) responsável',
    coordenadora: 'Coordenadora (consulta)'
  };

  rotuloPerfil = computed(() => {
    const role = this.auth.role();
    return role ? (this.rotulosPerfil[role] ?? role) : '';
  });

  /**
   * Turmas de matrícula do aluno, no cartão do usuário:
   * "Turma: T02 - Teste (Manhã)". Cursando mais de um módulo de estágio, ele vê
   * as duas ("Turmas: T01 - …, T02 - …").
   */
  turmaAluno = computed(() => {
    const u = this.auth.user();
    if (u?.role !== 'aluno') return '';

    const turmas = u.groups?.length
      ? u.groups.map(g => rotuloTurma(g.code, g.name, turnoDaTurma(g, u.shift))).filter(t => !!t)
      : [rotuloTurma(u.groupCode, u.groupName, u.shift)].filter(t => !!t);

    if (!turmas.length) return 'Sem turma vinculada';
    return `${turmas.length > 1 ? 'Turmas' : 'Turma'}: ${turmas.join(', ')}`;
  });

  /** A coordenadora navega igual ao professor, mas sem alterar nada. */
  somenteLeitura = this.auth.somenteLeitura;

  navItems = computed(() => {
    const role = this.auth.role();
    return this.allNav.filter(n => !role || n.roles.includes(role));
  });

  user = this.auth.user;
  role = this.auth.role;

  constructor(private auth: AuthService, private router: Router) {}

  logout(): void { this.auth.logout(); }
}
