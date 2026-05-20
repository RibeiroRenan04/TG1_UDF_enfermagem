import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTableModule } from '@angular/material/table';
import { MatChipsModule } from '@angular/material/chips';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { AttendanceService } from '../../core/services/attendance.service';
import { AttendanceRecord } from '../../core/models/models';
import { AuthService } from '../../core/services/auth.service';

@Component({
  selector: 'app-preceptor',
  standalone: true,
  imports: [CommonModule, MatCardModule, MatButtonModule, MatIconModule, MatTableModule, MatChipsModule, MatProgressSpinnerModule, MatSnackBarModule],
  templateUrl: './preceptor.component.html',
  styleUrls: ['./preceptor.component.scss']
})
export class PreceptorComponent implements OnInit {
  pending = signal<AttendanceRecord[]>([]);
  loading = signal(true);
  displayedColumns = ['date', 'student', 'type', 'distance', 'actions'];

  constructor(private attendanceService: AttendanceService, private auth: AuthService, private snackBar: MatSnackBar) {}

  ngOnInit(): void { this.load(); }

  load(): void {
    this.attendanceService.getAll().subscribe({
      next: (r) => { this.pending.set(r.filter(x => x.status === 'pendente' || x.status === 'irregular')); this.loading.set(false); },
      error: () => this.loading.set(false)
    });
  }

  validate(id: string, approve: boolean): void {
    this.attendanceService.validate(id, approve, approve ? undefined : 'Reprovado pelo preceptor').subscribe({
      next: () => { this.snackBar.open('Atualizado!', '', { duration: 2000 }); this.load(); },
      error: () => this.snackBar.open('Erro ao atualizar', '', { duration: 3000 })
    });
  }
}
