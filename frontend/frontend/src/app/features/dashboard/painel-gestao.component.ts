import { Component, OnInit, computed, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { DashboardService } from '../../core/services/dashboard.service';
import { PainelGestao, PresencaDia } from '../../core/models/painel-gestao';
import { mensagemErro } from '../../core/utils/api-error';
import { MatPaginatorModule } from '@angular/material/paginator';
import { Paginacao } from '../../core/utils/paginacao';
import { MatSortModule, Sort } from '@angular/material/sort';
import { ordenarLista } from '../../core/utils/ordenacao';

/**
 * Painel do professor e da secretaria. Cada bloco responde a uma pergunta,
 * da mais urgente para a de acompanhamento:
 *  1. Quem deveria estar em estágio agora e ainda não registrou? (hoje)
 *  2. A presença está caindo? (últimos 14 dias)
 *  3. Quem está ficando para trás? (turnos sem registro e carga horária)
 *  4. O que no cadastro impede o estágio de funcionar? (configuração)
 *
 * "Esperado" vem da mesma programação que libera o check-in, então feriado,
 * fim de semana e dias antes da entrada do aluno na turma nunca contam.
 */
@Component({
  selector: 'app-painel-gestao',
  standalone: true,
  imports: [CommonModule, RouterLink, MatCardModule, MatIconModule, MatButtonModule, MatProgressSpinnerModule, MatPaginatorModule,
    MatSortModule],
  templateUrl: './painel-gestao.component.html',
  styleUrls: ['./painel-gestao.component.scss']
})
export class PainelGestaoComponent implements OnInit {
  painel = signal<PainelGestao | null>(null);
  carregando = signal(true);
  erro = signal('');

  /** Dia com o ponteiro ou o foco do teclado no gráfico de presença. */
  diaEmFoco = signal<PresencaDia | null>(null);

  /** Ordem da tabela "Ver em tabela" do gráfico de presença. */
  readonly ordemDias = signal<Sort>({ active: 'data', direction: 'asc' });
  readonly diasOrdenados = computed(() => ordenarLista(this.painel()?.ultimosDias ?? [], this.ordemDias()));

  /**
   * Rampa ordinal das faixas de carga (azul, claro → escuro), validada na skill de dataviz.
   * Onze tons de um só azul não se distinguem; as faixas de 10% compartilham cinco tons por
   * proximidade (0–20%, 20–50%, 50–80%, 80–99%, concluída).
   */
  private readonly rampaCarga = ['#86b6ef', '#5598e7', '#2a78d6', '#1c5cab', '#104281'];
  private readonly tomDaFaixa = [0, 0, 1, 1, 1, 2, 2, 2, 3, 3, 4];

  corFaixa(indice: number): string {
    return this.rampaCarga[this.tomDaFaixa[indice] ?? this.rampaCarga.length - 1];
  }

  readonly alunosSemRegistro = computed(() => this.painel()?.hoje.alunosSemRegistro ?? []);
  /** 12 por página, a mesma altura dos cards vizinhos; o paginador mostra o restante. */
  readonly paginaSemRegistro = new Paginacao(this.alunosSemRegistro, 12);

  percentualHoje = computed(() => {
    const h = this.painel()?.hoje;
    return h && h.esperados > 0 ? Math.round(100 * h.registrados / h.esperados) : null;
  });

  /** Média ponderada do período: turnos registrados sobre turnos esperados. */
  mediaPeriodo = computed(() => {
    const dias = this.painel()?.ultimosDias ?? [];
    const esperados = dias.reduce((s, d) => s + d.esperados, 0);
    const feitos = dias.reduce((s, d) => s + d.registrados, 0);
    return esperados > 0 ? Math.round(100 * feitos / esperados) : null;
  });

  /** Último dia com estágio — o único rotulado direto no gráfico. */
  ultimoDiaComEstagio = computed(() =>
    [...(this.painel()?.ultimosDias ?? [])].reverse().find(d => d.percentual != null) ?? null);

  maiorRanking = computed(() =>
    Math.max(1, ...(this.painel()?.maisTurnosSemRegistro ?? []).map(a => a.quantidade)));

  maiorFaixa = computed(() =>
    Math.max(1, ...(this.painel()?.progressoCarga.faixas ?? []).map(f => f.alunos)));

  alunosComCarga = computed(() =>
    (this.painel()?.progressoCarga.faixas ?? []).reduce((s, f) => s + f.alunos, 0));

  pendenciasConfiguracao = computed(() =>
    (this.painel()?.configuracao ?? []).filter(a => a.quantidade > 0).length);

  constructor(private dashboard: DashboardService) {}

  ngOnInit(): void { this.carregar(); }

  carregar(): void {
    this.carregando.set(true);
    this.erro.set('');
    this.dashboard.getPainelGestao().subscribe({
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

  rotuloDia(iso: string): string {
    const [, mes, dia] = iso.split('-');
    return `${dia}/${mes}`;
  }

  diaDaSemana(iso: string): string {
    const [a, m, d] = iso.split('-').map(Number);
    return ['dom', 'seg', 'ter', 'qua', 'qui', 'sex', 'sáb'][new Date(a, m - 1, d).getDay()];
  }

  descricaoDia(d: PresencaDia): string {
    const data = `${this.diaDaSemana(d.data)}, ${this.rotuloDia(d.data)}`;
    return d.percentual == null
      ? `${data}: sem estágio programado`
      : `${data}: ${d.percentual}% — ${d.registrados} de ${d.esperados} turnos registrados`;
  }
}
