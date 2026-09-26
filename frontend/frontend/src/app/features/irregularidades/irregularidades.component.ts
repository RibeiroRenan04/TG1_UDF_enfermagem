import { Component, OnInit, ViewChild, computed, signal } from '@angular/core';
import { MatPaginatorModule } from '@angular/material/paginator';
import { Paginacao } from '../../core/utils/paginacao';
import { ordenarLista } from '../../core/utils/ordenacao';
import { Sort } from '@angular/material/sort';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatChipsModule } from '@angular/material/chips';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatTabsModule } from '@angular/material/tabs';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { IrregularitiesService } from '../../core/services/irregularities.service';
import { AuthService } from '../../core/services/auth.service';
import {
  Irregularity, IrregularityStatus, IrregularityType, IrregularitySummary
} from '../../core/models/models';
import { RegistrarIrregularidadeDialogComponent } from './registrar-irregularidade-dialog.component';
import { IrregularidadesPainelComponent, PRAZO_ATENCAO_DIAS } from './irregularidades-painel.component';
import { mensagemErro } from '../../core/utils/api-error';

/**
 * Painel único das irregularidades de ponto, com a visão de cada perfil:
 *   • aluno      → registra a ocorrência e acompanha o andamento;
 *   • preceptor  → toma ciência, observa e encaminha ao professor (não decide);
 *   • professor  → analisa e aprova ou nega, com parecer;
 *   • secretaria → apenas consulta.
 */
@Component({
  selector: 'app-irregularidades',
  standalone: true,
  imports: [
    CommonModule, FormsModule,
    MatCardModule, MatButtonModule, MatIconModule, MatChipsModule,
    MatFormFieldModule, MatInputModule, MatSelectModule, MatTabsModule,
    MatTooltipModule, MatProgressSpinnerModule, MatSnackBarModule, MatDialogModule,
    IrregularidadesPainelComponent, MatPaginatorModule
  ],
  templateUrl: './irregularidades.component.html',
  styleUrls: ['./irregularidades.component.scss']
})
export class IrregularidadesComponent implements OnInit {
  /** Indicadores da gestão: recarregados junto com a lista após cada decisão. */
  @ViewChild(IrregularidadesPainelComponent) painel?: IrregularidadesPainelComponent;

  itens = signal<Irregularity[]>([]);
  resumo = signal<IrregularitySummary | null>(null);
  loading = signal(true);
  /** Ocorrência que está sendo gravada no momento. */
  salvandoId = signal<string | null>(null);
  /** Observação/parecer digitado, por ocorrência. */
  notas: Record<string, string> = {};

  // Filtros como signals: com um campo comum, o computed da lista não percebia a
  // troca e clicar num contador de situação não filtrava nada.
  filtroStatus = signal<IrregularityStatus | 'todas'>('todas');
  busca = signal('');
  filtroTipo = signal<IrregularityType | 'todos'>('todos');

  role = this.auth.role;
  ehAluno = computed(() => this.auth.role() === 'aluno');
  ehPreceptor = computed(() => this.auth.role() === 'preceptor');
  ehProfessor = this.auth.ehProfessor;
  /** Professor e secretaria: veem os indicadores acima da lista. */
  ehGestao = this.auth.ehGestao;
  somenteLeitura = this.auth.somenteLeitura;

  readonly prazoAtencao = PRAZO_ATENCAO_DIAS;

  readonly tiposLabel: Record<IrregularityType, string> = {
    atraso: 'Atraso',
    esquecimento_checkin: 'Esqueci o check-in',
    esquecimento_checkout: 'Esqueci o check-out',
    fora_do_local: 'Registro fora do local',
    falta_justificada: 'Falta justificada',
    problema_tecnico: 'Problema técnico',
    outro: 'Outro'
  };

  readonly statusLabel: Record<IrregularityStatus, string> = {
    aguardando_preceptor: 'Aguardando o preceptor',
    aguardando_professor: 'Aguardando o professor',
    aprovada: 'Aprovada',
    negada: 'Negada'
  };

  /**
   * Lista filtrada. Nas situações em aberto, a mais antiga vem primeiro — é a
   * ordem em que a fila deve ser trabalhada; nas demais, a mais recente.
   */
  /** Ordem escolhida pelo usuário; "padrao" mantém a ordem da fila descrita acima. */
  readonly ordem = signal<Sort>({ active: 'padrao', direction: 'asc' });
  readonly camposOrdem = [
    { valor: 'padrao', rotulo: 'Padrão da fila' },
    { valor: 'occurredOn', rotulo: 'Data da ocorrência' },
    { valor: 'createdAt', rotulo: 'Aberta em' },
    { valor: 'studentName', rotulo: 'Aluno' },
    { valor: 'tipo', rotulo: 'Tipo' },
    { valor: 'espera', rotulo: 'Tempo na etapa' }
  ];

  mudarOrdem(campo?: string): void {
    this.ordem.update(o => campo
      ? { active: campo, direction: o.direction || 'asc' }
      : { ...o, direction: o.direction === 'asc' ? 'desc' : 'asc' });
  }

  private itensNaOrdemDaFila = computed(() => {
    const status = this.filtroStatus();
    const termo = this.normalizar(this.busca());
    const tipo = this.filtroTipo();

    const filtrados = this.itens().filter(i =>
      (status === 'todas' || i.status === status)
      && (tipo === 'todos' || i.type === tipo)
      && (!termo || this.normalizar(i.studentName).includes(termo) || (i.studentRgm ?? '').includes(termo)));

    const emAberto = status === 'aguardando_preceptor' || status === 'aguardando_professor';
    return [...filtrados].sort((a, b) => emAberto
      ? this.inicioDaEspera(a) - this.inicioDaEspera(b)
      : new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime());
  });

  itensFiltrados = computed(() => {
    const ordem = this.ordem();
    if (ordem.active === 'padrao') return this.itensNaOrdemDaFila();
    return ordenarLista(this.itensNaOrdemDaFila(), ordem, {
      tipo: i => this.tipoLabel(i.type),
      espera: i => this.diasEsperando(i)
    });
  });

  readonly paginacao = new Paginacao(this.itensFiltrados);

  temFiltroExtra = computed(() => !!this.busca().trim() || this.filtroTipo() !== 'todos');

  tiposDisponiveis = computed(() =>
    (Object.keys(this.tiposLabel) as IrregularityType[]).map(t => ({ valor: t, rotulo: this.tiposLabel[t] })));

  constructor(
    private service: IrregularitiesService,
    private auth: AuthService,
    private snackBar: MatSnackBar,
    private dialog: MatDialog
  ) {}

  ngOnInit(): void { this.carregar(); }

  carregar(): void {
    this.loading.set(true);
    this.service.getAll().subscribe({
      next: (r) => { this.itens.set(r); this.loading.set(false); },
      error: (err) => { this.loading.set(false); this.snackBar.open(mensagemErro(err, 'Erro ao carregar as ocorrências'), '', { duration: 4000 }); }
    });
    this.service.getSummary().subscribe({ next: (s) => this.resumo.set(s), error: () => {} });
    // Na abertura o painel ainda não existe e carrega sozinho; depois de uma
    // ciência ou decisão, a fila mudou e os indicadores precisam acompanhar.
    this.painel?.carregar();
  }

  aplicarFiltro(status: IrregularityStatus | 'todas'): void {
    this.filtroStatus.set(status);
  }

  /** Vindo dos indicadores: filtra e leva a lista para a vista. */
  filtrarPorStatus(status: IrregularityStatus): void {
    this.filtroStatus.set(status);
    this.rolarParaLista();
  }

  filtrarPorAluno(nome: string): void {
    this.filtroStatus.set('todas');
    this.busca.set(nome);
    this.rolarParaLista();
  }

  limparFiltros(): void {
    this.busca.set('');
    this.filtroTipo.set('todos');
    this.filtroStatus.set('todas');
  }

  private rolarParaLista(): void {
    setTimeout(() => document.getElementById('lista-ocorrencias')
      ?.scrollIntoView({ behavior: 'smooth', block: 'start' }));
  }

  // ── Aluno: registra a ocorrência ──────────────────────────────────────────
  abrirRegistro(): void {
    const ref = this.dialog.open(RegistrarIrregularidadeDialogComponent, {
      width: '560px', maxWidth: '95vw', maxHeight: '90vh', autoFocus: 'dialog'
    });
    ref.afterClosed().subscribe((criada: boolean) => {
      if (criada) {
        this.snackBar.open('Irregularidade registrada. O preceptor será notificado.', '',
          { duration: 4000, panelClass: 'snack-success' });
        this.carregar();
      }
    });
  }

  // ── Preceptor: ciência + observação, encaminhando ao professor ────────────
  darCiencia(item: Irregularity): void {
    this.salvandoId.set(item.id);
    this.service.preceptorReview(item.id, this.notas[item.id]?.trim() || undefined).subscribe({
      next: () => {
        this.salvandoId.set(null);
        delete this.notas[item.id];
        this.snackBar.open('Ciência registrada. Ocorrência encaminhada ao professor.', '',
          { duration: 4000, panelClass: 'snack-success' });
        this.carregar();
      },
      error: (err) => {
        this.salvandoId.set(null);
        this.snackBar.open(mensagemErro(err, 'Erro ao registrar a ciência'), '',
          { duration: 4000, panelClass: 'snack-error' });
      }
    });
  }

  // ── Professor: decisão final ──────────────────────────────────────────────
  decidir(item: Irregularity, aprovar: boolean): void {
    this.salvandoId.set(item.id);
    this.service.professorDecision(item.id, aprovar, this.notas[item.id]?.trim() || undefined).subscribe({
      next: () => {
        this.salvandoId.set(null);
        delete this.notas[item.id];
        this.snackBar.open(aprovar ? 'Ocorrência aprovada.' : 'Ocorrência negada.', '',
          { duration: 3500, panelClass: 'snack-success' });
        this.carregar();
      },
      error: (err) => {
        this.salvandoId.set(null);
        this.snackBar.open(mensagemErro(err, 'Erro ao registrar a decisão'), '',
          { duration: 4000, panelClass: 'snack-error' });
      }
    });
  }

  // ── Apoio ao template ─────────────────────────────────────────────────────
  /** O preceptor só age enquanto a ocorrência não foi decidida pelo professor. */
  podeDarCiencia(item: Irregularity): boolean {
    return this.ehPreceptor() && item.status !== 'aprovada' && item.status !== 'negada';
  }

  /** O professor decide qualquer ocorrência ainda em aberto. */
  podeDecidir(item: Irregularity): boolean {
    return this.ehProfessor() && item.status !== 'aprovada' && item.status !== 'negada';
  }

  tipoLabel(tipo: IrregularityType): string {
    return this.tiposLabel[tipo] ?? tipo;
  }

  labelStatus(status: IrregularityStatus): string {
    return this.statusLabel[status] ?? status;
  }

  iconeStatus(status: IrregularityStatus): string {
    switch (status) {
      case 'aprovada': return 'check_circle';
      case 'negada': return 'cancel';
      case 'aguardando_professor': return 'school';
      default: return 'hourglass_empty';
    }
  }

  /**
   * Desde quando a ocorrência espera a etapa atual: com o professor, desde a
   * ciência do preceptor; com o preceptor, desde a abertura.
   */
  private inicioDaEspera(item: Irregularity): number {
    const desde = item.status === 'aguardando_professor' && item.preceptorAcknowledgedAt
      ? item.preceptorAcknowledgedAt : item.createdAt;
    return new Date(desde).getTime();
  }

  /** Deixa claro de que etapa é a espera: "hoje" sozinho parecia a data de abertura. */
  rotuloEspera(item: Irregularity): string {
    const dias = this.diasEsperando(item) ?? 0;
    const tempo = dias === 0 ? 'desde hoje' : dias === 1 ? 'há 1 dia' : `há ${dias} dias`;
    return item.status === 'aguardando_professor'
      ? `Com o professor ${tempo}`
      : `Com o preceptor ${tempo}`;
  }

  /** Dias parada na etapa atual; nulo quando já foi decidida. */
  diasEsperando(item: Irregularity): number | null {
    if (item.status === 'aprovada' || item.status === 'negada') return null;
    return Math.max(0, Math.floor((Date.now() - this.inicioDaEspera(item)) / 86_400_000));
  }

  private normalizar(texto: string | null | undefined): string {
    return (texto ?? '').normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase().trim();
  }

  /** Etapa atual do fluxo (1 a 3), para a trilha exibida no cartão. */
  etapa(item: Irregularity): number {
    if (item.status === 'aprovada' || item.status === 'negada') return 3;
    if (item.status === 'aguardando_professor') return 2;
    return 1;
  }
}
