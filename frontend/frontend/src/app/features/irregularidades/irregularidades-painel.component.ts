import { Component, EventEmitter, OnInit, Output, computed, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';
import { IrregularitiesService } from '../../core/services/irregularities.service';
import { IrregularidadesPainel } from '../../core/models/irregularidades-painel';
import { IrregularityStatus } from '../../core/models/models';
import { mensagemErro } from '../../core/utils/api-error';

/** A partir de quantos dias parada uma ocorrência pede atenção. */
export const PRAZO_ATENCAO_DIAS = 3;

type Balde = IrregularidadesPainel['evolucao'][number];

/**
 * Indicadores das irregularidades para o professor e a coordenadora. Cada bloco
 * responde a uma pergunta:
 *  1. O que espera minha decisão, e há quanto tempo?
 *  2. Onde a fila emperra — no preceptor ou em mim?
 *  3. As ocorrências estão aumentando?
 *  4. Qual a causa? Tipo, unidade (fora do local concentrado numa unidade
 *     costuma ser coordenada ou raio errado) e aluno recorrente.
 *
 * Clicar num indicador filtra a lista de ocorrências logo abaixo.
 */
@Component({
  selector: 'app-irregularidades-painel',
  standalone: true,
  imports: [CommonModule, RouterLink, MatCardModule, MatIconModule, MatButtonModule,
    MatProgressSpinnerModule, MatTooltipModule],
  templateUrl: './irregularidades-painel.component.html',
  styleUrls: ['./irregularidades-painel.component.scss']
})
export class IrregularidadesPainelComponent implements OnInit {
  @Output() filtrarStatus = new EventEmitter<IrregularityStatus>();
  @Output() filtrarAluno = new EventEmitter<string>();

  readonly periodos: { dias: number | null; rotulo: string }[] = [
    { dias: 30, rotulo: '30 dias' },
    { dias: 90, rotulo: '90 dias' },
    { dias: 180, rotulo: 'Semestre' },
    { dias: null, rotulo: 'Tudo' }
  ];
  readonly prazoAtencao = PRAZO_ATENCAO_DIAS;

  periodo = signal<number | null>(30);
  painel = signal<IrregularidadesPainel | null>(null);
  carregando = signal(true);
  erro = signal('');
  baldeEmFoco = signal<Balde | null>(null);

  rotuloPeriodo = computed(() => this.periodos.find(p => p.dias === this.periodo())?.rotulo ?? '');

  /** Variação das abertas contra a janela anterior de mesmo tamanho. */
  variacao = computed(() => {
    const p = this.painel();
    if (!p || p.abertasPeriodoAnterior == null) return null;
    return p.abertasNoPeriodo - p.abertasPeriodoAnterior;
  });

  /**
   * Topo do eixo em marcas limpas: a metade é um passo de 1, 2 ou 5 × 10ⁿ, então
   * o eixo mostra 0 / 5 / 10, 0 / 10 / 20, 0 / 50 / 100…
   */
  topoEvolucao = computed(() => {
    const maior = Math.max(0, ...(this.painel()?.evolucao ?? []).map(b => b.abertas));
    if (maior <= 2) return 2;
    const metade = Math.ceil(maior / 2);
    const potencia = Math.pow(10, Math.floor(Math.log10(metade)));
    const passo = [1, 2, 5, 10].map(m => m * potencia).find(v => v >= metade)!;
    return passo * 2;
  });

  ultimoBaldeComDado = computed(() =>
    [...(this.painel()?.evolucao ?? [])].reverse().find(b => b.abertas > 0) ?? null);

  maiorTipo = computed(() => Math.max(1, ...(this.painel()?.porTipo ?? []).map(t => t.total)));
  maiorUnidade = computed(() => Math.max(1, ...(this.painel()?.porUnidade ?? []).map(u => u.total)));

  constructor(private service: IrregularitiesService) {}

  ngOnInit(): void { this.carregar(); }

  escolherPeriodo(dias: number | null): void {
    if (this.periodo() === dias) return;
    this.periodo.set(dias);
    this.carregar();
  }

  carregar(): void {
    this.carregando.set(true);
    this.erro.set('');
    this.service.getPainel(this.periodo()).subscribe({
      next: (p) => { this.painel.set(p); this.carregando.set(false); },
      error: (err) => {
        this.carregando.set(false);
        this.erro.set(mensagemErro(err, 'Não foi possível carregar os indicadores.'));
      }
    });
  }

  largura(valor: number, maior: number): number {
    return Math.max(2, Math.round(100 * valor / maior));
  }

  altura(valor: number): number {
    return 100 * valor / this.topoEvolucao();
  }

  atrasada(dias: number | null | undefined): boolean {
    return dias != null && dias > PRAZO_ATENCAO_DIAS;
  }

  /** "Fora do local" é pelo menos metade das ocorrências da unidade. */
  suspeitaDeLocalizacao(u: { total: number; foraDoLocal: number }): boolean {
    return u.foraDoLocal >= 2 && u.foraDoLocal * 2 >= u.total;
  }

  diasTexto(dias: number | null | undefined): string {
    if (dias == null) return '—';
    return dias === 0 ? 'hoje' : dias === 1 ? 'há 1 dia' : `há ${dias} dias`;
  }

  mediaTexto(dias: number | null): string {
    if (dias == null) return '—';
    return dias < 1 ? 'menos de 1 dia' : `${dias.toLocaleString('pt-BR', { maximumFractionDigits: 1 })} dia(s)`;
  }
}
