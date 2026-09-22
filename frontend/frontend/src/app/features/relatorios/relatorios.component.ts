import { Component, OnInit, computed, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTableModule } from '@angular/material/table';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatChipsModule } from '@angular/material/chips';
import { MatTooltipModule } from '@angular/material/tooltip';
import { ReportsService } from '../../core/services/reports.service';
import { ReportRow, ReportTurma } from '../../core/models/models';
import { rotuloTurno } from '../../core/utils/turma';

/** Valores exibidos numa linha — de uma turma ou do total do aluno. */
type Valores = Pick<ReportTurma,
  'hours' | 'required' | 'progressPercent' | 'approved' | 'irregular' | 'pendencyDays' | 'pendencyHours'>;

/**
 * Linha da tabela. Aluno com uma turma ocupa uma linha só; com várias, uma linha
 * por turma e uma de total — é no total que o certificado é decidido.
 */
interface LinhaRelatorio {
  tipo: 'turma' | 'total';
  aluno: ReportRow;
  turma?: ReportTurma;
  /** Primeira linha do aluno: é nela que o nome aparece. */
  primeira: boolean;
  /** A linha mostra o certificado: a única do aluno ou a de total. */
  certificado: boolean;
  valores: Valores;
}

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
  displayedColumns = ['name', 'turma', 'hours', 'required', 'progress', 'approved', 'irregular', 'pendencies', 'certificate'];

  linhas = computed<LinhaRelatorio[]>(() => this.rows().flatMap(a => this.montarLinhas(a)));

  constructor(private reportsService: ReportsService) {}

  ngOnInit(): void {
    this.reportsService.get().subscribe({ next: (r) => { this.rows.set(r); this.loading.set(false); }, error: () => this.loading.set(false) });
  }

  private montarLinhas(a: ReportRow): LinhaRelatorio[] {
    const total: Valores = a;
    if (a.turmas.length <= 1)
      return [{ tipo: 'turma', aluno: a, turma: a.turmas[0], primeira: true, certificado: true, valores: total }];

    return [
      ...a.turmas.map((t, i): LinhaRelatorio =>
        ({ tipo: 'turma', aluno: a, turma: t, primeira: i === 0, certificado: false, valores: t })),
      { tipo: 'total', aluno: a, primeira: false, certificado: true, valores: total }
    ];
  }

  turno(t?: ReportTurma): string {
    return rotuloTurno(t?.shift);
  }

  /** Aviso no total quando parte das horas veio de registros sem turma atual. */
  dicaForaDasTurmas(l: LinhaRelatorio): string {
    const fora = l.aluno.hoursOutsideGroups;
    return l.certificado && fora > 0
      ? `Inclui ${fora.toLocaleString('pt-BR')} h de registros sem turma atual (ponto sem rodízio ou de turma anterior).`
      : '';
  }

  exportCsv(): void {
    const header = ['Aluno', 'RGM', 'Turma', 'Turno', 'Horas aprovadas', 'Exigidas', 'Progresso (%)', 'Aprovados',
                    'Irregulares', 'Dias pendentes', 'Horas pendentes', 'Certificado'];
    const lines = this.linhas().map(l => [
      l.aluno.fullName, l.aluno.rgm ?? '',
      l.tipo === 'total' ? 'TOTAL DO ALUNO' : (l.turma?.groupCode ?? ''),
      l.tipo === 'total' ? '' : this.turno(l.turma),
      l.valores.hours, l.valores.required, l.valores.progressPercent, l.valores.approved,
      l.valores.irregular, l.valores.pendencyDays, l.valores.pendencyHours,
      l.certificado ? (l.aluno.certificateReleased ? 'Sim' : 'Não') : ''
    ].join(';'));
    const csv = [header.join(';'), ...lines].join('\n');
    const blob = new Blob([csv], { type: 'text/csv;charset=utf-8;' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a'); a.href = url; a.download = 'relatorio.csv'; a.click();
    URL.revokeObjectURL(url);
  }
}
