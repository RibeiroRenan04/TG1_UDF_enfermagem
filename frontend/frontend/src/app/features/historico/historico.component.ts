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

@Component({
  selector: 'app-historico',
  standalone: true,
  imports: [
    CommonModule, RouterLink, MatCardModule, MatTableModule, MatChipsModule, MatIconModule,
    MatProgressSpinnerModule, MatButtonModule, MatTooltipModule, MatDialogModule, MatSnackBarModule
  ],
  templateUrl: './historico.component.html',
  styleUrls: ['./historico.component.scss']
})
export class HistoricoComponent implements OnInit {
  records = signal<AttendanceRecord[]>([]);
  loading = signal(true);
  displayedColumns = ['date', 'type', 'location', 'distance', 'status', 'actions'];

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
   * Abre o registro de irregularidade. A ocorrência segue para a ciência do
   * preceptor e, na sequência, para a decisão do professor responsável.
   */
  registrarIrregularidade(): void {
    const ref = this.dialog.open(RegistrarIrregularidadeDialogComponent, { width: '520px' });
    ref.afterClosed().subscribe((criada: boolean) => {
      if (criada) {
        this.snackBar.open('Irregularidade registrada. Acompanhe em "Irregularidades".', '',
          { duration: 5000, panelClass: 'snack-success' });
      }
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
