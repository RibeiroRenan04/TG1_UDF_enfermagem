import { Component, OnDestroy, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTableModule } from '@angular/material/table';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { UnidadesSaudeService } from '../../core/services/unidades-saude.service';
import { AuthService } from '../../core/services/auth.service';
import { Alocacao, Turno, UnidadeSaude } from '../../core/models/models';
import { mensagemErro } from '../../core/utils/api-error';

/** Visão geral das alocações de estagiários, com o histórico completo. */
@Component({
  selector: 'app-alocacoes',
  standalone: true,
  imports: [
    CommonModule, FormsModule, RouterLink,
    MatCardModule, MatButtonModule, MatIconModule, MatTableModule,
    MatFormFieldModule, MatInputModule, MatSelectModule, MatTooltipModule,
    MatProgressSpinnerModule, MatSnackBarModule, MatPaginatorModule
  ],
  templateUrl: './alocacoes.component.html',
  styleUrls: ['./alocacoes.component.scss']
})
export class AlocacoesComponent implements OnInit, OnDestroy {
  alocacoes = signal<Alocacao[]>([]);
  unidades = signal<UnidadeSaude[]>([]);
  loading = signal(true);

  /**
   * Totais do filtro inteiro, vindos da API. Antes a lista parava em 500 e o
   * contador, feito sobre o que tinha chegado, mostrava "500 ativas" com mais
   * de 600 alunos alocados.
   */
  total = signal(0);
  ativas = signal(0);

  /** Página atual (base zero, como o mat-paginator) e tamanho da página. */
  pagina = 0;
  tamanhoPagina = 50;
  readonly tamanhos = [25, 50, 100];

  filtroUnidade = '';
  /** A busca por nome/RGM também é feita na API: filtrar só a página escondia quem estava nas outras. */
  filtroTexto = '';
  filtroAtivo: boolean | null = true;
  filtroTurno: Turno | '' = '';
  filtroDe = '';
  filtroAte = '';

  colunas = ['estagiario', 'rgm', 'unidade', 'turno', 'inicio', 'fim', 'situacao', 'acoes'];

  readonly turnos: Turno[] = ['manha', 'tarde', 'noite'];

  podeEditar = this.auth.ehProfessor;

  /** Espera o usuário parar de digitar antes de consultar a API. */
  private buscaTimer?: ReturnType<typeof setTimeout>;

  constructor(
    private service: UnidadesSaudeService,
    private auth: AuthService,
    private snackBar: MatSnackBar
  ) {}

  ngOnInit(): void {
    this.service.getAll({ ativo: true }).subscribe({ next: (u) => this.unidades.set(u), error: () => {} });
    this.carregar();
  }

  ngOnDestroy(): void {
    clearTimeout(this.buscaTimer);
  }

  carregar(): void {
    this.loading.set(true);
    this.service.getAlocacoes({
      unidadeId: this.filtroUnidade || undefined,
      ativo: this.filtroAtivo ?? undefined,
      turno: this.filtroTurno || undefined,
      de: this.filtroDe || undefined,
      ate: this.filtroAte || undefined,
      busca: this.filtroTexto.trim() || undefined,
      pagina: this.pagina + 1,
      tamanhoPagina: this.tamanhoPagina
    }).subscribe({
      next: (p) => {
        this.alocacoes.set(p.itens);
        this.total.set(p.total);
        this.ativas.set(p.ativas);
        this.loading.set(false);
      },
      error: (err) => {
        this.loading.set(false);
        this.snackBar.open(mensagemErro(err, 'Erro ao carregar as alocações'), '', { duration: 4000 });
      }
    });
  }

  /** Filtro alterado: volta para a primeira página. */
  filtrar(): void {
    this.pagina = 0;
    this.carregar();
  }

  buscar(texto: string): void {
    this.filtroTexto = texto;
    clearTimeout(this.buscaTimer);
    this.buscaTimer = setTimeout(() => this.filtrar(), 350);
  }

  mudarPagina(e: PageEvent): void {
    this.pagina = e.pageIndex;
    this.tamanhoPagina = e.pageSize;
    this.carregar();
  }

  limparFiltros(): void {
    this.filtroUnidade = '';
    this.filtroTexto = '';
    this.filtroAtivo = true;
    this.filtroTurno = '';
    this.filtroDe = '';
    this.filtroAte = '';
    this.filtrar();
  }

  turnoLabel(t?: string): string {
    return ({ manha: 'Manhã', tarde: 'Tarde', noite: 'Noite' } as Record<string, string>)[t ?? ''] ?? '—';
  }

  encerrar(a: Alocacao): void {
    // Encerrar é por turno: as alocações do aluno em outros turnos continuam.
    if (!confirm(`Encerrar a alocação de ${a.estagiarioNome} em "${a.unidadeNome}" no turno da ` +
                 `${this.turnoLabel(a.turno).toLowerCase()}? O histórico é preservado e os outros ` +
                 'turnos não são afetados.')) return;

    this.service.encerrarAlocacao(a.unidadeId, a.estagiarioId, a.turno).subscribe({
      next: () => {
        this.snackBar.open('Alocação encerrada.', '', { duration: 3000, panelClass: 'snack-success' });
        this.carregar();
      },
      error: (err) => this.snackBar.open(mensagemErro(err, 'Erro ao encerrar'), '',
        { duration: 4000, panelClass: 'snack-error' })
    });
  }
}
