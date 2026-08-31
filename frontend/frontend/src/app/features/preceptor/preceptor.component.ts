import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTableModule } from '@angular/material/table';
import { MatChipsModule } from '@angular/material/chips';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { AttendanceService } from '../../core/services/attendance.service';
import { IrregularitiesService } from '../../core/services/irregularities.service';
import { AttendanceRecord, Irregularity } from '../../core/models/models';

/**
 * Painel do preceptor. Ele acompanha os registros de ponto dos alunos e as
 * ocorrências em aberto, mas não valida nem altera a situação de nenhum deles:
 * a decisão é do professor responsável (ver a tela de Irregularidades).
 */
@Component({
  selector: 'app-preceptor',
  standalone: true,
  imports: [
    CommonModule, RouterLink,
    MatCardModule, MatButtonModule, MatIconModule, MatTableModule,
    MatChipsModule, MatTooltipModule, MatProgressSpinnerModule, MatSnackBarModule
  ],
  templateUrl: './preceptor.component.html',
  styleUrls: ['./preceptor.component.scss']
})
export class PreceptorComponent implements OnInit {
  pending = signal<AttendanceRecord[]>([]);
  /** Ocorrências que ainda esperam a ciência do preceptor. */
  aguardandoCiencia = signal<Irregularity[]>([]);
  loading = signal(true);
  displayedColumns = ['date', 'student', 'type', 'distance', 'status'];

  constructor(
    private attendanceService: AttendanceService,
    private irregularities: IrregularitiesService,
    private snackBar: MatSnackBar
  ) {}

  ngOnInit(): void { this.load(); }

  load(): void {
    this.loading.set(true);
    this.attendanceService.getAll().subscribe({
      next: (r) => {
        this.pending.set(r.filter(x => x.status === 'pendente' || x.status === 'irregular'));
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.snackBar.open('Erro ao carregar os registros', '', { duration: 4000 });
      }
    });

    this.irregularities.getAll('aguardando_preceptor').subscribe({
      next: (r) => this.aguardandoCiencia.set(r),
      error: () => {}
    });
  }

  rotuloStatus(status: string): string {
    switch (status) {
      case 'aprovado': return 'Aprovado';
      case 'irregular': return 'Irregular';
      default: return 'Pendente';
    }
  }
}
