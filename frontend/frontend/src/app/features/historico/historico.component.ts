import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatTableModule } from '@angular/material/table';
import { MatChipsModule } from '@angular/material/chips';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatButtonModule } from '@angular/material/button';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { AttendanceService } from '../../core/services/attendance.service';
import { AttendanceRecord } from '../../core/models/models';
import { RegistrarIrregularidadeDialogComponent } from '../irregularidades/registrar-irregularidade-dialog.component';
import { MatSortModule } from '@angular/material/sort';
import { Ordenacao } from '../../core/utils/ordenacao';

@Component({
  selector: 'app-historico',
  standalone: true,
  imports: [
    CommonModule, RouterLink, MatCardModule, MatTableModule, MatChipsModule, MatIconModule,
    MatProgressSpinnerModule, MatButtonModule, MatTooltipModule, MatDialogModule, MatSnackBarModule,
    MatSortModule
  ],
  templateUrl: './historico.component.html',
  styleUrls: ['./historico.component.scss']
})
export class HistoricoComponent implements OnInit {
  records = signal<AttendanceRecord[]>([]);
  loading = signal(true);
  displayedColumns = ['date', 'shift', 'type', 'location', 'distance', 'status', 'actions'];

  readonly ordenacao = new Ordenacao(this.records, {
    date: r => r.recordedAt,
    shift: r => this.rotuloTurno(r.shift),
    location: r => r.remoteActivityId ? (r.remoteActivityTitle || 'Atividade remota') : r.locationName,
    distance: r => r.remoteActivityId ? null : r.distanceMeters,
    status: r => this.rotuloStatus(r.status)
  }, { active: 'date', direction: 'desc' });

  constructor(
    private attendanceService: AttendanceService,
    private dialog: MatDialog,
    private snackBar: MatSnackBar
  ) {}

  ngOnInit(): void { this.carregar(); }

  carregar(): void {
    this.loading.set(true);
    this.attendanceService.getAll().subscribe({
      next: (r) => { this.records.set(r); this.loading.set(false); },
      error: () => this.loading.set(false)
    });
  }

  /**
   * Abre o registro de irregularidade. Quando parte de uma linha da tabela, o
   * ponto já vai vinculado — o aluno não precisa procurá-lo numa lista.
   *
   * A ocorrência segue para a ciência do preceptor e, na sequência, para a
   * decisão do professor responsável.
   */
  registrarIrregularidade(registro?: AttendanceRecord): void {
    if (registro?.hasOpenIrregularity) {
      this.snackBar.open(this.motivoBloqueio(registro), '', { duration: 5000 });
      return;
    }

    const ref = this.dialog.open(RegistrarIrregularidadeDialogComponent, {
      width: '560px', maxWidth: '95vw', maxHeight: '90vh', autoFocus: 'dialog', data: { registro }
    });
    ref.afterClosed().subscribe((criada: boolean) => {
      if (criada) {
        this.snackBar.open('Irregularidade registrada. Acompanhe em "Irregularidades".', '',
          { duration: 5000, panelClass: 'snack-success' });
        // Recarrega para o ponto já aparecer com a contestação em andamento.
        this.carregar();
      }
    });
  }

  /**
   * O botão de contestação fica inativo enquanto houver uma ocorrência em
   * análise para o mesmo ponto: só volta a valer se o professor recusá-la.
   */
  podeContestar(r: AttendanceRecord): boolean {
    return r.status !== 'aprovado' && !r.hasOpenIrregularity;
  }

  motivoBloqueio(r: AttendanceRecord): string {
    if (!r.hasOpenIrregularity) return 'Registrar irregularidade sobre este ponto';
    return r.irregularityStatus === 'aprovada'
      ? 'Este ponto já teve uma irregularidade aprovada.'
      : 'Já existe uma irregularidade em análise para este ponto. '
        + 'Você poderá abrir outra se o professor recusar a atual.';
  }

  rotuloStatus(status: string): string {
    switch (status) {
      case 'aprovado': return 'Aprovado';
      case 'irregular': return 'Irregular';
      default: return 'Pendente';
    }
  }

  rotuloTurno(turno?: string): string {
    return ({ manha: 'Manhã', tarde: 'Tarde', noite: 'Noite' } as Record<string, string>)[turno ?? ''] ?? '—';
  }
}
