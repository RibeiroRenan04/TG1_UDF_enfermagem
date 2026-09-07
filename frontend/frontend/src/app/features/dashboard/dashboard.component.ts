import { Component, OnInit, computed, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { RouterLink } from '@angular/router';
import { DashboardService } from '../../core/services/dashboard.service';
import { AuthService } from '../../core/services/auth.service';
import { DashboardStats, PendingStatus } from '../../core/models/models';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, MatCardModule, MatIconModule, MatButtonModule, MatChipsModule, MatProgressSpinnerModule, RouterLink],
  templateUrl: './dashboard.component.html',
  styleUrls: ['./dashboard.component.scss']
})
export class DashboardComponent implements OnInit {
  stats = signal<DashboardStats | null>(null);
  loading = signal(true);
  role = this.auth.role;

  /** Perfis que enxergam o contador de alunos e os totais da turma. */
  ehGestao = this.auth.ehGestao;

  /**
   * Avisos do card "Status Pendentes" — prazos, dias sem registro e o andamento
   * das irregularidades, centralizados para o aluno não perder o prazo por falta
   * de alerta.
   */
  statusPendentes = computed<PendingStatus[]>(() => this.stats()?.pendingStatuses ?? []);

  /** O card só some quando não há nenhum aviso a mostrar. */
  temPendencias = computed(() => this.statusPendentes().length > 0);

  constructor(private dashService: DashboardService, private auth: AuthService) {}

  ngOnInit(): void {
    this.dashService.getStats().subscribe({
      next: (s) => { this.stats.set(s); this.loading.set(false); },
      error: () => this.loading.set(false)
    });
  }

  iconeAviso(kind: string): string {
    switch (kind) {
      case 'dias_sem_registro': return 'event_busy';
      case 'irregularidade_em_analise': return 'hourglass_top';
      case 'irregularidade_negada': return 'cancel';
      case 'aguardando_ciencia': return 'fact_check';
      case 'aguardando_decisao': return 'gavel';
      default: return 'notifications';
    }
  }
}
