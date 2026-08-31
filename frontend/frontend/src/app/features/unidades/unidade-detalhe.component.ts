import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTableModule } from '@angular/material/table';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { FormsModule } from '@angular/forms';
import { UnidadesSaudeService } from '../../core/services/unidades-saude.service';
import { AuthService } from '../../core/services/auth.service';
import { UnidadeSaude, Alocacao } from '../../core/models/models';
import { AlocarEstagiarioDialogComponent } from './alocar-estagiario-dialog.component';
import { STATUS_GEO, ORIGEM_COORDENADAS } from './status-geocodificacao';

/** Detalhes da unidade e os estagiários alocados nela. */
@Component({
  selector: 'app-unidade-detalhe',
  standalone: true,
  imports: [
    CommonModule, FormsModule, RouterLink,
    MatCardModule, MatButtonModule, MatIconModule, MatTableModule, MatTooltipModule,
    MatSlideToggleModule, MatProgressSpinnerModule, MatSnackBarModule, MatDialogModule
  ],
  templateUrl: './unidade-detalhe.component.html',
  styleUrls: ['./unidade-detalhe.component.scss']
})
export class UnidadeDetalheComponent implements OnInit {
  unidade = signal<UnidadeSaude | null>(null);
  alocacoes = signal<Alocacao[]>([]);
  loading = signal(true);
  mostrarEncerradas = false;

  colunas = ['nome', 'rgm', 'periodo', 'inicio', 'situacao', 'acoes'];

  readonly statusGeo = STATUS_GEO;
  readonly origens = ORIGEM_COORDENADAS;

  podeEditar = this.auth.ehProfessor;

  private unidadeId = '';

  constructor(
    private route: ActivatedRoute,
    private service: UnidadesSaudeService,
    private auth: AuthService,
    private snackBar: MatSnackBar,
    private dialog: MatDialog
  ) {}

  ngOnInit(): void {
    this.unidadeId = this.route.snapshot.paramMap.get('id') ?? '';
    this.carregar();
  }

  carregar(): void {
    this.loading.set(true);
    this.service.get(this.unidadeId).subscribe({
      next: (u) => { this.unidade.set(u); this.loading.set(false); },
      error: () => {
        this.loading.set(false);
        this.snackBar.open('Unidade não encontrada', '', { duration: 4000 });
      }
    });
    this.carregarAlocacoes();
  }

  carregarAlocacoes(): void {
    this.service.getEstagiarios(this.unidadeId, this.mostrarEncerradas).subscribe({
      next: (a) => this.alocacoes.set(a),
      error: () => {}
    });
  }

  /** Link para o OpenStreetMap — visualização do mapa, não geocodificação. */
  urlMapa(u: UnidadeSaude): string {
    return `https://www.openstreetmap.org/?mlat=${u.latitude}&mlon=${u.longitude}#map=17/${u.latitude}/${u.longitude}`;
  }

  alocar(): void {
    const u = this.unidade();
    if (!u) return;

    const ref = this.dialog.open(AlocarEstagiarioDialogComponent, {
      width: '620px', maxHeight: '85vh', data: { unidade: u }
    });
    ref.afterClosed().subscribe((alocado: boolean) => {
      if (alocado) { this.carregar(); }
    });
  }

  encerrar(a: Alocacao): void {
    if (!confirm(`Encerrar a alocação de ${a.estagiarioNome} nesta unidade? O histórico é preservado.`)) return;

    this.service.encerrarAlocacao(this.unidadeId, a.estagiarioId).subscribe({
      next: () => {
        this.snackBar.open('Alocação encerrada.', '', { duration: 3000, panelClass: 'snack-success' });
        this.carregar();
      },
      error: (err) => this.snackBar.open(err?.error?.message ?? 'Erro ao encerrar', '',
        { duration: 4000, panelClass: 'snack-error' })
    });
  }

  geocodificar(): void {
    this.snackBar.open('Consultando a localização…', '', { duration: 2500 });
    this.service.geocodificar(this.unidadeId).subscribe({
      next: (r) => {
        this.snackBar.open(r.sucesso ? 'Localização atualizada.' : (r.mensagem ?? 'Não localizada.'),
          '', { duration: 5000, panelClass: r.sucesso ? 'snack-success' : 'snack-error' });
        this.carregar();
      },
      error: (err) => this.snackBar.open(err?.error?.message ?? 'Erro ao geocodificar', '',
        { duration: 4000, panelClass: 'snack-error' })
    });
  }

  rotuloStatus(s?: string): string { return this.statusGeo[s ?? 'pendente']?.rotulo ?? '—'; }
  classeStatus(s?: string): string { return this.statusGeo[s ?? 'pendente']?.classe ?? ''; }
  iconeStatus(s?: string): string { return this.statusGeo[s ?? 'pendente']?.icone ?? 'help'; }
  rotuloOrigem(o?: string): string { return o ? (this.origens[o] ?? o) : '—'; }

  turnoLabel(t?: string): string {
    return t ? ({ manha: 'Manhã', tarde: 'Tarde', noite: 'Noite' } as Record<string, string>)[t] ?? t : '—';
  }
}
