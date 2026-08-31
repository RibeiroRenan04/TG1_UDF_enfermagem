import { Component, OnInit, computed, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatChipsModule } from '@angular/material/chips';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatTabsModule } from '@angular/material/tabs';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { IrregularitiesService } from '../../core/services/irregularities.service';
import { AuthService } from '../../core/services/auth.service';
import {
  Irregularity, IrregularityStatus, IrregularityType, IrregularitySummary
} from '../../core/models/models';
import { RegistrarIrregularidadeDialogComponent } from './registrar-irregularidade-dialog.component';

/**
 * Painel único das irregularidades de ponto, com a visão de cada perfil:
 *   • aluno      → registra a ocorrência e acompanha o andamento;
 *   • preceptor  → toma ciência, observa e encaminha ao professor (não decide);
 *   • professor  → analisa e aprova ou nega, com parecer;
 *   • coordenadora → apenas consulta.
 */
@Component({
  selector: 'app-irregularidades',
  standalone: true,
  imports: [
    CommonModule, FormsModule,
    MatCardModule, MatButtonModule, MatIconModule, MatChipsModule,
    MatFormFieldModule, MatInputModule, MatSelectModule, MatTabsModule,
    MatTooltipModule, MatProgressSpinnerModule, MatSnackBarModule, MatDialogModule
  ],
  templateUrl: './irregularidades.component.html',
  styleUrls: ['./irregularidades.component.scss']
})
export class IrregularidadesComponent implements OnInit {
  itens = signal<Irregularity[]>([]);
  resumo = signal<IrregularitySummary | null>(null);
  loading = signal(true);
  /** Ocorrência que está sendo gravada no momento. */
  salvandoId = signal<string | null>(null);
  /** Observação/parecer digitado, por ocorrência. */
  notas: Record<string, string> = {};
  filtroStatus: IrregularityStatus | 'todas' = 'todas';

  role = this.auth.role;
  ehAluno = computed(() => this.auth.role() === 'aluno');
  ehPreceptor = computed(() => this.auth.role() === 'preceptor');
  ehProfessor = this.auth.ehProfessor;
  somenteLeitura = this.auth.somenteLeitura;

  readonly tiposLabel: Record<IrregularityType, string> = {
    atraso: 'Atraso',
    esquecimento_checkin: 'Esqueci o check-in',
    esquecimento_checkout: 'Esqueci o check-out',
    fora_do_local: 'Registro fora do local',
    falta_justificada: 'Falta justificada',
    problema_tecnico: 'Problema técnico',
    outro: 'Outro'
  };

  readonly statusLabel: Record<IrregularityStatus, string> = {
    aguardando_preceptor: 'Aguardando o preceptor',
    aguardando_professor: 'Aguardando o professor',
    aprovada: 'Aprovada',
    negada: 'Negada'
  };

  itensFiltrados = computed(() => {
    const todos = this.itens();
    return this.filtroStatus === 'todas'
      ? todos
      : todos.filter(i => i.status === this.filtroStatus);
  });

  constructor(
    private service: IrregularitiesService,
    private auth: AuthService,
    private snackBar: MatSnackBar,
    private dialog: MatDialog
  ) {}

  ngOnInit(): void { this.carregar(); }

  carregar(): void {
    this.loading.set(true);
    this.service.getAll().subscribe({
      next: (r) => { this.itens.set(r); this.loading.set(false); },
      error: () => { this.loading.set(false); this.snackBar.open('Erro ao carregar as ocorrências', '', { duration: 4000 }); }
    });
    this.service.getSummary().subscribe({ next: (s) => this.resumo.set(s), error: () => {} });
  }

  aplicarFiltro(status: IrregularityStatus | 'todas'): void {
    this.filtroStatus = status;
  }

  // ── Aluno: registra a ocorrência ──────────────────────────────────────────
  abrirRegistro(): void {
    const ref = this.dialog.open(RegistrarIrregularidadeDialogComponent, { width: '520px' });
    ref.afterClosed().subscribe((criada: boolean) => {
      if (criada) {
        this.snackBar.open('Irregularidade registrada. O preceptor será notificado.', '',
          { duration: 4000, panelClass: 'snack-success' });
        this.carregar();
      }
    });
  }

  // ── Preceptor: ciência + observação, encaminhando ao professor ────────────
  darCiencia(item: Irregularity): void {
    this.salvandoId.set(item.id);
    this.service.preceptorReview(item.id, this.notas[item.id]?.trim() || undefined).subscribe({
      next: () => {
        this.salvandoId.set(null);
        delete this.notas[item.id];
        this.snackBar.open('Ciência registrada. Ocorrência encaminhada ao professor.', '',
          { duration: 4000, panelClass: 'snack-success' });
        this.carregar();
      },
      error: (err) => {
        this.salvandoId.set(null);
        this.snackBar.open(err?.error?.message ?? 'Erro ao registrar a ciência', '',
          { duration: 4000, panelClass: 'snack-error' });
      }
    });
  }

  // ── Professor: decisão final ──────────────────────────────────────────────
  decidir(item: Irregularity, aprovar: boolean): void {
    this.salvandoId.set(item.id);
    this.service.professorDecision(item.id, aprovar, this.notas[item.id]?.trim() || undefined).subscribe({
      next: () => {
        this.salvandoId.set(null);
        delete this.notas[item.id];
        this.snackBar.open(aprovar ? 'Ocorrência aprovada.' : 'Ocorrência negada.', '',
          { duration: 3500, panelClass: 'snack-success' });
        this.carregar();
      },
      error: (err) => {
        this.salvandoId.set(null);
        this.snackBar.open(err?.error?.message ?? 'Erro ao registrar a decisão', '',
          { duration: 4000, panelClass: 'snack-error' });
      }
    });
  }

  // ── Apoio ao template ─────────────────────────────────────────────────────
  /** O preceptor só age enquanto a ocorrência não foi decidida pelo professor. */
  podeDarCiencia(item: Irregularity): boolean {
    return this.ehPreceptor() && item.status !== 'aprovada' && item.status !== 'negada';
  }

  /** O professor decide qualquer ocorrência ainda em aberto. */
  podeDecidir(item: Irregularity): boolean {
    return this.ehProfessor() && item.status !== 'aprovada' && item.status !== 'negada';
  }

  tipoLabel(tipo: IrregularityType): string {
    return this.tiposLabel[tipo] ?? tipo;
  }

  labelStatus(status: IrregularityStatus): string {
    return this.statusLabel[status] ?? status;
  }

  iconeStatus(status: IrregularityStatus): string {
    switch (status) {
      case 'aprovada': return 'check_circle';
      case 'negada': return 'cancel';
      case 'aguardando_professor': return 'school';
      default: return 'hourglass_empty';
    }
  }

  /** Etapa atual do fluxo (1 a 3), para a trilha exibida no cartão. */
  etapa(item: Irregularity): number {
    if (item.status === 'aprovada' || item.status === 'negada') return 3;
    if (item.status === 'aguardando_professor') return 2;
    return 1;
  }
}
