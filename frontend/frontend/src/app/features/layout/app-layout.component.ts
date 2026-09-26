import { Component, ViewChild, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { toSignal } from '@angular/core/rxjs-interop';
import { map } from 'rxjs';
import { BreakpointObserver } from '@angular/cdk/layout';
import { Router, RouterOutlet, RouterLink, RouterLinkActive } from '@angular/router';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatSidenav, MatSidenavModule } from '@angular/material/sidenav';
import { MatListModule } from '@angular/material/list';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatDividerModule } from '@angular/material/divider';
import { MatTooltipModule } from '@angular/material/tooltip';
import { AuthService } from '../../core/services/auth.service';
import { LiberarCamada, VoltarService } from '../../core/services/voltar.service';
import { rotuloTurma, turnoDaTurma } from '../../core/utils/turma';

/** Abaixo desta largura o menu deixa de ficar fixo ao lado e abre por cima. */
const TELA_COMPACTA = '(max-width: 1023.98px)';

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
  @ViewChild('sidenav') private sidenav?: MatSidenav;

  private readonly voltar = inject(VoltarService);

  /**
   * Celular e tablet: o menu fixo ao lado espremia a página (tabelas cortadas,
   * campos sobrepostos). Nessas telas ele abre por cima, em tela cheia no celular.
   */
  readonly compacto = toSignal(
    inject(BreakpointObserver).observe(TELA_COMPACTA).pipe(map(r => r.matches)),
    { initialValue: window.matchMedia(TELA_COMPACTA).matches });

  /** Menu aberto no computador (lá ele pode ser recolhido pelo botão da barra). */
  readonly menuAberto = signal(true);

  /** Tira do histórico a entrada que o menu aberto ocupa (ver VoltarService). */
  private liberarMenu?: LiberarCamada;
  /** O menu fechou porque uma página foi escolhida: a navegação ocupa a entrada dele. */
  private escolheuPagina = false;

  private readonly allNav: NavItem[] = [
    { path: '/app',             label: 'Painel',            icon: 'dashboard',        roles: ['aluno','preceptor','supervisor','secretaria'] },
    { path: '/app/check-in',    label: 'Registrar presença', icon: 'location_on',      roles: ['aluno'] },
    { path: '/app/historico',   label: 'Meu histórico',      icon: 'assignment',       roles: ['aluno'] },
    { path: '/app/irregularidades', label: 'Irregularidades', icon: 'fact_check',      roles: ['aluno','preceptor','supervisor','secretaria'] },
    { path: '/app/certificados', label: 'Certificados',      icon: 'workspace_premium', roles: ['aluno','supervisor','secretaria'] },
    { path: '/app/acompanhamentos', label: 'Acompanhamentos', icon: 'description',    roles: ['aluno','preceptor','supervisor','secretaria'] },
    { path: '/app/preceptor',   label: 'Meus alunos',        icon: 'star',             roles: ['preceptor'] },
    // "Locais" saiu do menu: era a mesma tabela de "Unidades de saúde", em outra
    // tela. A rota antiga continua existindo e redireciona para cá.
    { path: '/app/unidades',    label: 'Unidades de saúde',  icon: 'domain',           roles: ['aluno','supervisor','secretaria'] },
    { path: '/app/alocacoes',   label: 'Alocações',          icon: 'assignment_ind',   roles: ['supervisor','secretaria'] },
    { path: '/app/rodizios',    label: 'Rodízios',           icon: 'calendar_today',   roles: ['supervisor','secretaria'] },
    { path: '/app/atividades-remotas', label: 'Atividades remotas', icon: 'home_work',  roles: ['aluno','supervisor','secretaria'] },
    { path: '/app/excecoes',    label: 'Calendário', icon: 'event_busy',   roles: ['supervisor','secretaria'] },
    { path: '/app/usuarios',    label: 'Usuários',           icon: 'people',           roles: ['supervisor','secretaria'] },
    { path: '/app/relatorios',  label: 'Relatórios',         icon: 'bar_chart',        roles: ['supervisor','secretaria'] }
  ];

  /** Rótulos de perfil: o identificador técnico não é o nome usado na faculdade. */
  private readonly rotulosPerfil: Record<string, string> = {
    aluno: 'Aluno(a)',
    preceptor: 'Preceptor(a)',
    supervisor: 'Professor(a) responsável',
    secretaria: 'Secretaria (consulta)'
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

  /** A secretaria navega igual ao professor, mas sem alterar nada. */
  somenteLeitura = this.auth.somenteLeitura;

  navItems = computed(() => {
    const role = this.auth.role();
    return this.allNav.filter(n => !role || n.roles.includes(role));
  });

  user = this.auth.user;
  role = this.auth.role;

  constructor(private auth: AuthService, private router: Router) {}

  alternarMenu(): void {
    if (this.compacto()) this.sidenav?.toggle();
    else this.menuAberto.update(v => !v);
  }

  /** Com o menu por cima, o voltar do celular fecha o menu em vez de sair da página. */
  aoMudarMenu(aberto: boolean): void {
    if (aberto && this.compacto()) {
      this.liberarMenu = this.voltar.abrir(() => this.sidenav?.close());
    } else if (!aberto) {
      this.liberarMenu?.({ semVoltar: this.escolheuPagina });
      this.liberarMenu = undefined;
      this.escolheuPagina = false;
    }
  }

  /**
   * Escolher uma página fecha o menu. A navegação substitui (replaceUrl) a
   * entrada do menu no histórico: voltar leva à página em que se estava antes.
   */
  aoEscolherItem(item: NavItem): void {
    if (!this.compacto()) return;
    this.escolheuPagina = !this.ativo(item);
    this.sidenav?.close();
  }

  ativo(item: NavItem): boolean {
    return this.router.isActive(item.path,
      { paths: 'exact', queryParams: 'ignored', fragment: 'ignored', matrixParams: 'ignored' });
  }

  logout(): void {
    this.escolheuPagina = true;
    this.sidenav?.close();
    this.auth.logout();
  }
}
