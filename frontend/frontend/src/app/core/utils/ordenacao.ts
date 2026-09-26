import { Signal, computed, signal } from '@angular/core';
import { Sort } from '@angular/material/sort';

/** Como ler o valor de cada coluna ordenável; sem acessor, vale a propriedade de mesmo nome. */
export type AcessoresOrdenacao<T> = Record<string, (item: T) => unknown>;

const COLLATOR = new Intl.Collator('pt-BR', { numeric: true, sensitivity: 'base' });

function vazio(v: unknown): boolean {
  return v === null || v === undefined || v === '' || (typeof v === 'number' && isNaN(v));
}

/**
 * Compara dois valores de célula: números e datas pela grandeza, textos em
 * ordem alfabética do português ("Érica" junto de "Erica", "T2" antes de "T10").
 */
export function compararValores(a: unknown, b: unknown): number {
  if (typeof a === 'number' && typeof b === 'number') return a - b;
  if (typeof a === 'boolean' && typeof b === 'boolean') return Number(a) - Number(b);
  if (a instanceof Date && b instanceof Date) return a.getTime() - b.getTime();
  return COLLATOR.compare(String(a), String(b));
}

/** Nova lista ordenada; vazios ficam sempre no fim, em qualquer direção. */
export function ordenarLista<T>(lista: T[], sort: Sort, acessores: AcessoresOrdenacao<T> = {}): T[] {
  if (!sort.active || !sort.direction) return lista;
  const ler = acessores[sort.active] ?? ((item: T) => (item as Record<string, unknown>)[sort.active]);
  const fator = sort.direction === 'asc' ? 1 : -1;

  return [...lista].sort((x, y) => {
    const a = ler(x), b = ler(y);
    if (vazio(a) || vazio(b)) return vazio(a) === vazio(b) ? 0 : vazio(a) ? 1 : -1;
    return compararValores(a, b) * fator;
  });
}

/**
 * Ordenação em memória para as tabelas com `matSort`. Clicar no cabeçalho
 * alterna crescente/decrescente; a lista ordenada vai para a paginação:
 *
 *   readonly ordenacao = new Ordenacao(this.filtrados, { aluno: c => c.studentName });
 *   readonly paginacao = new Paginacao(this.ordenacao.itens);
 *
 *   <table mat-table matSort matSortDisableClear (matSortChange)="ordenacao.mudar($event)">
 *     <th mat-header-cell *matHeaderCellDef mat-sort-header="aluno">Aluno</th>
 */
export class Ordenacao<T> {
  readonly estado = signal<Sort>({ active: '', direction: '' });

  readonly itens = computed(() => ordenarLista(this.fonte(), this.estado(), this.acessores));

  constructor(
    private readonly fonte: Signal<T[]>,
    private readonly acessores: AcessoresOrdenacao<T> = {},
    inicial?: Sort
  ) {
    if (inicial) this.estado.set(inicial);
  }

  mudar(sort: Sort): void {
    this.estado.set(sort);
  }
}
