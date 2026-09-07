import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTableModule } from '@angular/material/table';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatChipsModule } from '@angular/material/chips';
import { MatTooltipModule } from '@angular/material/tooltip';
import { ReportsService } from '../../core/services/reports.service';
import { ReportRow } from '../../core/models/models';

@Component({
  selector: 'app-relatorios',
  standalone: true,
  imports: [
    CommonModule, MatCardModule, MatButtonModule, MatIconModule, MatTableModule,
    MatProgressSpinnerModule, MatChipsModule, MatTooltipModule
  ],
  templateUrl: './relatorios.component.html',
  styleUrls: ['./relatorios.component.scss']
})
export class RelatoriosComponent implements OnInit {
  rows = signal<ReportRow[]>([]);
  loading = signal(true);
  displayedColumns = ['name', 'hours', 'required', 'progress', 'approved', 'irregular', 'pendencies', 'certificate'];

  constructor(private reportsService: ReportsService) {}

  ngOnInit(): void {
    this.reportsService.get().subscribe({ next: (r) => { this.rows.set(r); this.loading.set(false); }, error: () => this.loading.set(false) });
  }

  exportCsv(): void {
    const header = ['Nome', 'Horas', 'Exigidas', 'Progresso (%)', 'Aprovados', 'Irregulares',
                    'Dias pendentes', 'Horas pendentes', 'Certificado'];
    const lines = this.rows().map(r => [
      r.fullName, r.hours, r.required, r.progressPercent, r.approved, r.irregular,
      r.pendencyDays, r.pendencyHours, r.certificateReleased ? 'Sim' : 'Não'
    ].join(';'));
    const csv = [header.join(';'), ...lines].join('\n');
    const blob = new Blob([csv], { type: 'text/csv;charset=utf-8;' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a'); a.href = url; a.download = 'relatorio.csv'; a.click();
    URL.revokeObjectURL(url);
  }
}
