import { Signal, computed, signal } from '@angular/core';
import { PageEvent } from '@angular/material/paginator';

export const TAMANHO_PAGINA = 20;
export const OPCOES_TAMANHO_PAGINA = [20, 50, 100];

/**
 * Paginação em memória sobre uma lista já filtrada. Se a lista encolher (um filtro novo), a
 * página volta para a última existente em vez de ficar vazia.
 */
export class Paginacao<T> {
  private readonly indiceEscolhido = signal(0);
  readonly tamanho = signal(TAMANHO_PAGINA);
  readonly opcoes = OPCOES_TAMANHO_PAGINA;

  readonly total = computed(() => this.itens().length);

  readonly indice = computed(() => {
    const ultima = Math.max(0, Math.ceil(this.total() / this.tamanho()) - 1);
    return Math.min(this.indiceEscolhido(), ultima);
  });

  readonly pagina = computed(() => {
    const inicio = this.indice() * this.tamanho();
    return this.itens().slice(inicio, inicio + this.tamanho());
  });

  constructor(private readonly itens: Signal<T[]>, tamanhoInicial = TAMANHO_PAGINA) {
    this.tamanho.set(tamanhoInicial);
  }

  mudar(evento: PageEvent): void {
    this.tamanho.set(evento.pageSize);
    this.indiceEscolhido.set(evento.pageIndex);
  }

  inicio(): void {
    this.indiceEscolhido.set(0);
  }
}

/** Texto sem acento e em minúsculas, para a busca achar "João" digitando "joao". */
export function normalizarBusca(texto: string | null | undefined): string {
  return (texto ?? '').normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase().trim();
}
