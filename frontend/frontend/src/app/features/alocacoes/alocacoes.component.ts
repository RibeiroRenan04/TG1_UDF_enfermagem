import { Component, OnInit, computed, signal } from '@angular/core';
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
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { UnidadesSaudeService } from '../../core/services/unidades-saude.service';
import { AuthService } from '../../core/services/auth.service';
import { Alocacao, UnidadeSaude } from '../../core/models/models';

/** Visão geral das alocações de estagiários, com o histórico completo. */
@Component({
  selector: 'app-alocacoes',
  standalone: true,
  imports: [
    CommonModule, FormsModule, RouterLink,
    MatCardModule, MatButtonModule, MatIconModule, MatTableModule,
    MatFormFieldModule, MatInputModule, MatSelectModule, MatTooltipModule,
    MatProgressSpinnerModule, MatSnackBarModule
  ],
  templateUrl: './alocacoes.component.html',
  styleUrls: ['./alocacoes.component.scss']
})
export class AlocacoesComponent implements OnInit {
  alocacoes = signal<Alocacao[]>([]);
  unidades = signal<UnidadeSaude[]>([]);
  loading = signal(true);

  filtroUnidade = '';
  filtroTexto = '';
  filtroAtivo: boolean | null = true;
  filtroDe = '';
  filtroAte = '';

  colunas = ['estagiario', 'rgm', 'unidade', 'inicio', 'fim', 'situacao', 'acoes'];

  podeEditar = this.auth.ehProfessor;

  /** O filtro por nome/RGM é aplicado aqui: a API filtra por id, não por texto livre. */
  filtradas = computed(() => {
    const termo = this.filtroTexto.trim().toLowerCase();
    if (!termo) return this.alocacoes();
    return this.alocacoes().filter(a =>
      a.estagiarioNome.toLowerCase().includes(termo) ||
      (a.estagiarioRgm ?? '').includes(termo));
  });

  ativas = computed(() => this.alocacoes().filter(a => a.ativo).length);

  constructor(
    private service: UnidadesSaudeService,
    private auth: AuthService,
    private snackBar: MatSnackBar
  ) {}

  ngOnInit(): void {
    this.service.getAll({ ativo: true }).subscribe({ next: (u) => this.unidades.set(u), error: () => {} });
    this.carregar();
  }

  carregar(): void {
    this.loading.set(true);
    this.service.getAlocacoes({
      unidadeId: this.filtroUnidade || undefined,
      ativo: this.filtroAtivo ?? undefined,
      de: this.filtroDe || undefined,
      ate: this.filtroAte || undefined
    }).subscribe({
      next: (a) => { this.alocacoes.set(a); this.loading.set(false); },
      error: () => {
        this.loading.set(false);
        this.snackBar.open('Erro ao carregar as alocações', '', { duration: 4000 });
      }
    });
  }

  limparFiltros(): void {
    this.filtroUnidade = '';
    this.filtroTexto = '';
    this.filtroAtivo = true;
    this.filtroDe = '';
    this.filtroAte = '';
    this.carregar();
  }

  encerrar(a: Alocacao): void {
    if (!confirm(`Encerrar a alocação de ${a.estagiarioNome} em "${a.unidadeNome}"? O histórico é preservado.`)) return;

    this.service.encerrarAlocacao(a.unidadeId, a.estagiarioId).subscribe({
      next: () => {
        this.snackBar.open('Alocação encerrada.', '', { duration: 3000, panelClass: 'snack-success' });
        this.carregar();
      },
      error: (err) => this.snackBar.open(err?.error?.message ?? 'Erro ao encerrar', '',
        { duration: 4000, panelClass: 'snack-error' })
    });
  }
}
