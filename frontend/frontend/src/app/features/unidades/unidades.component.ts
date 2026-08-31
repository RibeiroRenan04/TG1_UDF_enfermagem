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
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { UnidadesSaudeService } from '../../core/services/unidades-saude.service';
import { AuthService } from '../../core/services/auth.service';
import { UnidadeSaude, StatusGeocodificacao } from '../../core/models/models';
import { UnidadeFormDialogComponent } from './unidade-form-dialog.component';
import { STATUS_GEO } from './status-geocodificacao';

/** Lista das unidades de saúde, com os filtros da tela. */
@Component({
  selector: 'app-unidades',
  standalone: true,
  imports: [
    CommonModule, FormsModule, RouterLink,
    MatCardModule, MatButtonModule, MatIconModule, MatTableModule,
    MatFormFieldModule, MatInputModule, MatSelectModule, MatTooltipModule,
    MatProgressSpinnerModule, MatSnackBarModule, MatDialogModule
  ],
  templateUrl: './unidades.component.html',
  styleUrls: ['./unidades.component.scss']
})
export class UnidadesComponent implements OnInit {
  unidades = signal<UnidadeSaude[]>([]);
  loading = signal(true);

  filtroNome = '';
  filtroTipo = '';
  filtroCidade = '';
  filtroAtivo: boolean | null = true;
  filtroStatus: StatusGeocodificacao | '' = '';

  colunas = ['nome', 'tipo', 'cidade', 'coordenadas', 'estagiarios', 'status', 'acoes'];

  readonly statusGeo = STATUS_GEO;
  readonly opcoesStatus: StatusGeocodificacao[] =
    ['pendente', 'processando', 'sucesso', 'revisao_manual', 'nao_encontrado', 'erro'];

  /** Só o professor altera; a coordenadora consulta. */
  podeEditar = this.auth.ehProfessor;
  somenteLeitura = this.auth.somenteLeitura;

  /** Unidades que precisam de conferência da localização. */
  precisamRevisao = computed(() =>
    this.unidades().filter(u => this.statusGeo[u.statusGeocodificacao ?? 'pendente']?.exigeAtencao).length);

  readonly tipos = ['UBS', 'Hospital', 'UPA', 'Policlínica', 'CAPS', 'Instituição de ensino', 'Outro'];

  constructor(
    private service: UnidadesSaudeService,
    private auth: AuthService,
    private snackBar: MatSnackBar,
    private dialog: MatDialog
  ) {}

  ngOnInit(): void { this.carregar(); }

  carregar(): void {
    this.loading.set(true);
    this.service.getAll({
      nome: this.filtroNome || undefined,
      tipo: this.filtroTipo || undefined,
      cidade: this.filtroCidade || undefined,
      ativo: this.filtroAtivo ?? undefined,
      statusGeocodificacao: this.filtroStatus || undefined
    }).subscribe({
      next: (u) => { this.unidades.set(u); this.loading.set(false); },
      error: () => {
        this.loading.set(false);
        this.snackBar.open('Erro ao carregar as unidades', '', { duration: 4000 });
      }
    });
  }

  limparFiltros(): void {
    this.filtroNome = '';
    this.filtroTipo = '';
    this.filtroCidade = '';
    this.filtroAtivo = true;
    this.filtroStatus = '';
    this.carregar();
  }

  novaUnidade(): void {
    const ref = this.dialog.open(UnidadeFormDialogComponent, { width: '640px', maxHeight: '90vh' });
    ref.afterClosed().subscribe((criada: UnidadeSaude | null) => {
      if (criada) {
        this.snackBar.open(`Unidade "${criada.nome}" cadastrada.`, '',
          { duration: 4000, panelClass: 'snack-success' });
        this.carregar();
      }
    });
  }

  editar(u: UnidadeSaude): void {
    const ref = this.dialog.open(UnidadeFormDialogComponent, {
      width: '640px', maxHeight: '90vh', data: { unidade: u }
    });
    ref.afterClosed().subscribe((salva: UnidadeSaude | null) => {
      if (salva) {
        this.snackBar.open('Unidade atualizada.', '', { duration: 3000, panelClass: 'snack-success' });
        this.carregar();
      }
    });
  }

  geocodificar(u: UnidadeSaude): void {
    this.snackBar.open(`Consultando a localização de "${u.nome}"…`, '', { duration: 2500 });
    this.service.geocodificar(u.id).subscribe({
      next: (r) => {
        this.snackBar.open(
          r.sucesso ? `Localização encontrada (${r.precisao ?? 'sem detalhe'}).`
                    : r.mensagem ?? 'Não foi possível localizar a unidade.',
          '', { duration: 5000, panelClass: r.sucesso ? 'snack-success' : 'snack-error' });
        this.carregar();
      },
      error: (err) => this.snackBar.open(err?.error?.message ?? 'Erro ao geocodificar', '',
        { duration: 4000, panelClass: 'snack-error' })
    });
  }

  desativar(u: UnidadeSaude): void {
    if (!confirm(`Desativar a unidade "${u.nome}"? Ela deixa de receber novas alocações; o histórico é preservado.`)) return;
    this.service.desativar(u.id).subscribe({
      next: () => {
        this.snackBar.open('Unidade desativada.', '', { duration: 3000, panelClass: 'snack-success' });
        this.carregar();
      },
      error: (err) => this.snackBar.open(err?.error?.message ?? 'Erro ao desativar', '',
        { duration: 5000, panelClass: 'snack-error' })
    });
  }

  rotuloStatus(s?: string): string { return this.statusGeo[s ?? 'pendente']?.rotulo ?? (s ?? '—'); }
  classeStatus(s?: string): string { return this.statusGeo[s ?? 'pendente']?.classe ?? ''; }
  iconeStatus(s?: string): string { return this.statusGeo[s ?? 'pendente']?.icone ?? 'help'; }
}
