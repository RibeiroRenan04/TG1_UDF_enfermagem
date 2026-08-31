import { Routes } from '@angular/router';
import { authGuard, guestGuard, firstAccessGuard, termsGuard } from './core/guards/auth.guard';
import { roleGuard } from './core/guards/role.guard';

export const routes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    loadComponent: () => import('./features/landing/landing.component').then(m => m.LandingComponent)
  },
  {
    path: 'auth',
    canActivate: [guestGuard],
    loadComponent: () => import('./features/auth/auth.component').then(m => m.AuthComponent)
  },
  {
    path: 'primeiro-acesso',
    canActivate: [firstAccessGuard],
    loadComponent: () => import('./features/primeiro-acesso/primeiro-acesso.component').then(m => m.PrimeiroAcessoComponent)
  },
  {
    path: 'termo-responsabilidade',
    canActivate: [termsGuard],
    loadComponent: () => import('./features/termo/termo-responsabilidade.component').then(m => m.TermoResponsabilidadeComponent)
  },
  {
    path: 'app',
    canActivate: [authGuard],
    loadComponent: () => import('./features/layout/app-layout.component').then(m => m.AppLayoutComponent),
    children: [
      {
        path: '',
        loadComponent: () => import('./features/dashboard/dashboard.component').then(m => m.DashboardComponent)
      },
      {
        path: 'check-in',
        canActivate: [roleGuard(['aluno'])],
        loadComponent: () => import('./features/check-in/check-in.component').then(m => m.CheckInComponent)
      },
      {
        path: 'historico',
        canActivate: [roleGuard(['aluno'])],
        loadComponent: () => import('./features/historico/historico.component').then(m => m.HistoricoComponent)
      },
      {
        path: 'acompanhamentos',
        loadComponent: () => import('./features/acompanhamentos/acompanhamentos.component').then(m => m.AcompanhamentosComponent)
      },
      {
        path: 'irregularidades',
        loadComponent: () => import('./features/irregularidades/irregularidades.component').then(m => m.IrregularidadesComponent)
      },
      {
        path: 'preceptor',
        canActivate: [roleGuard(['preceptor'])],
        loadComponent: () => import('./features/preceptor/preceptor.component').then(m => m.PreceptorComponent)
      },
      // ── Unidades de saúde ──
      // Leitura liberada a todos os perfis (o aluno precisa ver a própria unidade);
      // as ações de escrita são bloqueadas na API e escondidas nas telas.
      {
        path: 'unidades',
        loadComponent: () => import('./features/unidades/unidades.component').then(m => m.UnidadesComponent)
      },
      {
        path: 'unidades/importar',
        canActivate: [roleGuard(['supervisor'])],
        loadComponent: () => import('./features/unidades/importar-unidades.component').then(m => m.ImportarUnidadesComponent)
      },
      {
        path: 'unidades/revisao',
        canActivate: [roleGuard(['supervisor', 'coordenadora'])],
        loadComponent: () => import('./features/unidades/revisao-localizacao.component').then(m => m.RevisaoLocalizacaoComponent)
      },
      {
        path: 'unidades/:id',
        loadComponent: () => import('./features/unidades/unidade-detalhe.component').then(m => m.UnidadeDetalheComponent)
      },
      {
        path: 'alocacoes',
        canActivate: [roleGuard(['supervisor', 'coordenadora'])],
        loadComponent: () => import('./features/alocacoes/alocacoes.component').then(m => m.AlocacoesComponent)
      },
      {
        path: 'locais',
        canActivate: [roleGuard(['supervisor', 'coordenadora'])],
        loadComponent: () => import('./features/locais/locais.component').then(m => m.LocaisComponent)
      },
      {
        path: 'rodizios',
        canActivate: [roleGuard(['supervisor', 'coordenadora'])],
        loadComponent: () => import('./features/rodizios/rodizios.component').then(m => m.RodiziosComponent)
      },
      {
        path: 'usuarios',
        canActivate: [roleGuard(['supervisor', 'coordenadora'])],
        loadComponent: () => import('./features/usuarios/usuarios.component').then(m => m.UsuariosComponent)
      },
      {
        path: 'relatorios',
        canActivate: [roleGuard(['supervisor', 'coordenadora'])],
        loadComponent: () => import('./features/relatorios/relatorios.component').then(m => m.RelatoriosComponent)
      },
      {
        path: 'certificados',
        canActivate: [roleGuard(['aluno', 'supervisor', 'coordenadora'])],
        loadComponent: () => import('./features/certificados/certificados.component').then(m => m.CertificadosComponent)
      }
    ]
  },
  { path: '**', redirectTo: '/app' }
];
