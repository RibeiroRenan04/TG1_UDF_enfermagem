import { Injectable, Provider } from '@angular/core';
import { DateAdapter, MAT_DATE_FORMATS, MAT_DATE_LOCALE, MatDateFormats } from '@angular/material/core';

const FUSO = 'America/Sao_Paulo';
const INVALIDA = 'invalida';
const ISO = /^(\d{4})-(\d{2})-(\d{2})/;

const pad = (n: number, tam = 2) => `${n}`.padStart(tam, '0');

/** Data de hoje no horário de Brasília, em "yyyy-MM-dd" (independe do fuso do aparelho). */
export function hojeIso(): string {
  return new Intl.DateTimeFormat('sv-SE', { timeZone: FUSO }).format(new Date());
}

/**
 * Datepicker em pt-BR cujo valor é a própria string "yyyy-MM-dd" que a API espera: os
 * formulários continuam enviando o mesmo valor, sem converter Date nem cair no dia anterior
 * por causa do fuso. Aceita digitação em "dd/MM/aaaa".
 */
@Injectable()
export class DataIsoAdapter extends DateAdapter<string> {
  constructor() {
    super();
    this.setLocale('pt-BR');
  }

  private paraDate(valor: string): Date {
    const [, a, m, d] = ISO.exec(valor)!;
    const data = new Date(Number(a), Number(m) - 1, Number(d));
    data.setFullYear(Number(a));
    return data;
  }

  private deDate(d: Date): string {
    return `${pad(d.getFullYear(), 4)}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
  }

  private nomes(opcoes: Intl.DateTimeFormatOptions, datas: Date[]): string[] {
    const fmt = new Intl.DateTimeFormat('pt-BR', { ...opcoes, timeZone: 'UTC' });
    return datas.map(d => fmt.format(d).replace('.', ''));
  }

  getYear(d: string): number { return Number(d.slice(0, 4)); }
  getMonth(d: string): number { return Number(d.slice(5, 7)) - 1; }
  getDate(d: string): number { return Number(d.slice(8, 10)); }
  getDayOfWeek(d: string): number { return this.paraDate(d).getDay(); }

  getMonthNames(estilo: 'long' | 'short' | 'narrow'): string[] {
    return this.nomes({ month: estilo }, Array.from({ length: 12 }, (_, i) => new Date(Date.UTC(2017, i, 1))));
  }

  getDateNames(): string[] {
    return Array.from({ length: 31 }, (_, i) => `${i + 1}`);
  }

  getDayOfWeekNames(estilo: 'long' | 'short' | 'narrow'): string[] {
    return this.nomes({ weekday: estilo }, Array.from({ length: 7 }, (_, i) => new Date(Date.UTC(2017, 0, i + 1))));
  }

  getYearName(d: string): string { return `${this.getYear(d)}`; }
  getFirstDayOfWeek(): number { return 0; }

  getNumDaysInMonth(d: string): number {
    return new Date(this.getYear(d), this.getMonth(d) + 1, 0).getDate();
  }

  clone(d: string): string { return d; }

  createDate(ano: number, mes: number, dia: number): string {
    const data = new Date(ano, mes, dia);
    data.setFullYear(ano);
    if (data.getMonth() !== mes) throw Error(`Data inválida: ${dia}/${mes + 1}/${ano}.`);
    return this.deDate(data);
  }

  today(): string { return hojeIso(); }

  parse(valor: unknown): string | null {
    if (valor == null || valor === '') return null;
    if (valor instanceof Date) return isNaN(valor.getTime()) ? this.invalid() : this.deDate(valor);
    if (typeof valor !== 'string') return this.invalid();

    const texto = valor.trim();
    if (ISO.test(texto)) return this.valida(texto.slice(0, 10));

    const br = /^(\d{1,2})\/(\d{1,2})\/(\d{4})$/.exec(texto) ?? /^(\d{2})(\d{2})(\d{4})$/.exec(texto);
    if (!br) return this.invalid();
    const [, d, m, a] = br.map(Number);
    return this.valida(`${pad(a, 4)}-${pad(m)}-${pad(d)}`);
  }

  private valida(iso: string): string {
    const d = this.paraDate(iso);
    return this.deDate(d) === iso ? iso : this.invalid();
  }

  format(d: string, formato: string): string {
    if (!this.isValid(d)) throw Error('DataIsoAdapter: data inválida.');
    const data = this.paraDate(d);
    switch (formato) {
      case 'mes-ano':
        return new Intl.DateTimeFormat('pt-BR', { month: 'short', year: 'numeric' }).format(data).replace('.', '');
      case 'mes-ano-extenso':
        return new Intl.DateTimeFormat('pt-BR', { month: 'long', year: 'numeric' }).format(data);
      case 'extenso':
        return new Intl.DateTimeFormat('pt-BR', { dateStyle: 'long' }).format(data);
      default:
        return `${pad(data.getDate())}/${pad(data.getMonth() + 1)}/${pad(data.getFullYear(), 4)}`;
    }
  }

  addCalendarYears(d: string, anos: number): string { return this.addCalendarMonths(d, anos * 12); }

  addCalendarMonths(d: string, meses: number): string {
    const alvo = new Date(this.getYear(d), this.getMonth(d) + meses, 1);
    const ultimo = new Date(alvo.getFullYear(), alvo.getMonth() + 1, 0).getDate();
    return this.deDate(new Date(alvo.getFullYear(), alvo.getMonth(), Math.min(this.getDate(d), ultimo)));
  }

  addCalendarDays(d: string, dias: number): string {
    return this.deDate(new Date(this.getYear(d), this.getMonth(d), this.getDate(d) + dias));
  }

  toIso8601(d: string): string { return d; }

  isDateInstance(obj: unknown): boolean {
    return obj === INVALIDA || (typeof obj === 'string' && ISO.test(obj));
  }

  isValid(d: string): boolean {
    return d !== INVALIDA && ISO.test(d) && !isNaN(this.paraDate(d).getTime());
  }

  invalid(): string { return INVALIDA; }

  override deserialize(valor: unknown): string | null {
    if (valor == null || valor === '') return null;
    if (valor instanceof Date) return this.deDate(valor);
    if (typeof valor === 'string' && ISO.test(valor)) return valor.slice(0, 10);
    return super.deserialize(valor);
  }
}

export const DATA_BR_FORMATOS: MatDateFormats = {
  parse: { dateInput: 'dd/MM/yyyy' },
  display: {
    dateInput: 'dd/MM/yyyy',
    monthYearLabel: 'mes-ano',
    dateA11yLabel: 'extenso',
    monthYearA11yLabel: 'mes-ano-extenso'
  }
};

export function provideDataBr(): Provider[] {
  return [
    { provide: MAT_DATE_LOCALE, useValue: 'pt-BR' },
    { provide: DateAdapter, useClass: DataIsoAdapter },
    { provide: MAT_DATE_FORMATS, useValue: DATA_BR_FORMATOS }
  ];
}
