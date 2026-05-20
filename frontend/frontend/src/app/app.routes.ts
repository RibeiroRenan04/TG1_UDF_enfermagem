import { Routes } from '@angular/router';
import { authGuard, guestGuard, firstAccessGuard } from './core/guards/auth.guard';
import { roleGuard } from './core/guards/role.guard';

export const routes: Routes = [
  { path: '', redirectTo: '/app', pathMatch: 'full' },
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
        path: 'preceptor',
        canActivate: [roleGuard(['preceptor'])],
        loadComponent: () => import('./features/preceptor/preceptor.component').then(m => m.PreceptorComponent)
      },
      {
        path: 'locais',
        canActivate: [roleGuard(['supervisor'])],
        loadComponent: () => import('./features/locais/locais.component').then(m => m.LocaisComponent)
      },
      {
        path: 'rodizios',
        canActivate: [roleGuard(['supervisor'])],
        loadComponent: () => import('./features/rodizios/rodizios.component').then(m => m.RodiziosComponent)
      },
      {
        path: 'usuarios',
        canActivate: [roleGuard(['supervisor'])],
        loadComponent: () => import('./features/usuarios/usuarios.component').then(m => m.UsuariosComponent)
      },
      {
        path: 'relatorios',
        canActivate: [roleGuard(['supervisor'])],
        loadComponent: () => import('./features/relatorios/relatorios.component').then(m => m.RelatoriosComponent)
      }
    ]
  },
  { path: '**', redirectTo: '/app' }
];
