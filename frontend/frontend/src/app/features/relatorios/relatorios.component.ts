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
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatPaginatorModule } from '@angular/material/paginator';
import { Paginacao, normalizarBusca } from '../../core/utils/paginacao';
import { Ordenacao } from '../../core/utils/ordenacao';
import { MatSortModule } from '@angular/material/sort';

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
    MatProgressSpinnerModule, MatChipsModule, MatTooltipModule,
    MatFormFieldModule, MatInputModule, MatPaginatorModule, MatSortModule
  ],
  templateUrl: './relatorios.component.html',
  styleUrls: ['./relatorios.component.scss']
})
export class RelatoriosComponent implements OnInit {
  rows = signal<ReportRow[]>([]);
  loading = signal(true);
  displayedColumns = ['name', 'turma', 'hours', 'required', 'progress', 'approved', 'irregular', 'pendencies', 'certificate'];

  readonly busca = signal('');
  readonly filtrados = computed(() => {
    const termo = normalizarBusca(this.busca());
    return termo
      ? this.rows().filter(a => [a.fullName, a.rgm, ...a.turmas.map(t => t.groupCode)]
          .some(c => normalizarBusca(c).includes(termo)))
      : this.rows();
  });
  /**
   * Ordenado por aluno (pelos valores totais dele), para as linhas das turmas de
   * um mesmo aluno continuarem juntas. A exportação sai na mesma ordem da tela.
   */
  readonly ordenacao = new Ordenacao(this.filtrados, {
    name: a => a.fullName,
    turma: a => a.turmas[0]?.groupCode,
    hours: a => a.hours,
    required: a => a.required,
    progress: a => a.progressPercent,
    approved: a => a.approved,
    irregular: a => a.irregular,
    pendencies: a => a.pendencyDays,
    certificate: a => a.certificateReleased
  });
  /** Paginado por aluno, para as linhas de um mesmo aluno não se separarem. */
  readonly paginacao = new Paginacao(this.ordenacao.itens);

  linhas = computed<LinhaRelatorio[]>(() => this.paginacao.pagina().flatMap(a => this.montarLinhas(a)));

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

  readonly gerandoPdf = signal(false);

  /** O arquivo sai com o mesmo recorte da tela: a busca aplicada vai junto e aparece no cabeçalho. */
  async baixarPdf(): Promise<void> {
    this.gerandoPdf.set(true);
    try {
      // jsPDF e SheetJS são pesados: só carregam quando alguém exporta.
      const { gerarPdf } = await import('./relatorio-exportacao');
      gerarPdf({ alunos: this.ordenacao.itens(), busca: this.busca().trim() }, await this.carregarLogo());
    } finally {
      this.gerandoPdf.set(false);
    }
  }

  async baixarXlsx(): Promise<void> {
    const { gerarXlsx } = await import('./relatorio-exportacao');
    gerarXlsx({ alunos: this.ordenacao.itens(), busca: this.busca().trim() });
  }

  /** Logo da UDF em data URL para embutir no PDF; sem ela o relatório sai só com o nome. */
  private async carregarLogo(): Promise<string | null> {
    try {
      const blob = await (await fetch('assets/logo.png')).blob();
      return await new Promise<string>((ok, falha) => {
        const leitor = new FileReader();
        leitor.onload = () => ok(leitor.result as string);
        leitor.onerror = falha;
        leitor.readAsDataURL(blob);
      });
    } catch {
      return null;
    }
  }
}
