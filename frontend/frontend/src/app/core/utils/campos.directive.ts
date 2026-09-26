import { AfterContentInit, Directive, ElementRef, HostListener, inject, input } from '@angular/core';
import { AbstractControl, NgControl, ValidationErrors, ValidatorFn } from '@angular/forms';
import { HoraInputDirective } from './hora-input.directive';

/**
 * Máscaras dos campos de texto. Cada uma diz quais caracteres o campo aceita,
 * como o valor é exibido e qual teclado o celular abre.
 *
 *   cep       00000-000                 (teclado numérico)
 *   telefone  (00) 0000-0000 / (00) 00000-0000
 *   inteiro   só dígitos                (RGM, matrícula…)
 *   uf        duas letras maiúsculas
 *   nome      letras, espaço, apóstrofo, hífen e ponto (nomes de pessoa, cidade)
 *   codigo    letras maiúsculas, dígitos e hífen (código de presença, código de turma)
 */
export type TipoMascara = 'cep' | 'telefone' | 'inteiro' | 'uf' | 'nome' | 'codigo';

interface Mascara {
  /** Um caractere digitado vale para o campo? */
  aceita: (c: string) => boolean;
  /** Quantidade máxima de caracteres aceitos (sem contar a pontuação da máscara). */
  max?: number;
  maiusculas?: boolean;
  formatar?: (limpo: string) => string;
  inputmode: 'numeric' | 'text' | 'tel';
  validar?: (limpo: string) => string | null;
}

const DIGITO = (c: string) => c >= '0' && c <= '9';

const MASCARAS: Record<TipoMascara, Mascara> = {
  cep: {
    aceita: DIGITO, max: 8, inputmode: 'numeric',
    formatar: d => d.length > 5 ? `${d.slice(0, 5)}-${d.slice(5)}` : d,
    validar: d => d.length === 8 ? null : 'O CEP tem 8 dígitos (00000-000).'
  },
  telefone: {
    aceita: DIGITO, max: 11, inputmode: 'tel',
    formatar: d => {
      if (!d) return '';
      if (d.length <= 2) return `(${d}`;
      const resto = d.slice(2);
      if (resto.length <= 4) return `(${d.slice(0, 2)}) ${resto}`;
      const corte = resto.length > 8 ? 5 : 4;
      return `(${d.slice(0, 2)}) ${resto.slice(0, corte)}-${resto.slice(corte)}`;
    },
    validar: d => d.length >= 10 ? null : 'Informe DDD e número: (00) 0000-0000.'
  },
  inteiro: { aceita: DIGITO, inputmode: 'numeric' },
  uf: {
    aceita: c => /^[a-z]$/i.test(c), max: 2, maiusculas: true, inputmode: 'text',
    validar: d => d.length === 2 ? null : 'Informe a sigla com 2 letras.'
  },
  nome: { aceita: c => /^[\p{L} '’.-]$/u.test(c), inputmode: 'text' },
  codigo: { aceita: c => /^[a-z0-9-]$/i.test(c), maiusculas: true, inputmode: 'text' }
};

/**
 * Aplica a máscara fora do campo — para valores que vieram do cadastro antigo
 * ("70.790-060", "df") saírem no formato que a API aceita.
 */
export function formatarComMascara(tipo: TipoMascara, valor?: string | null): string {
  const m = MASCARAS[tipo];
  let limpo = [...(valor ?? '')].filter(ch => m.aceita(ch)).join('');
  if (m.maiusculas) limpo = limpo.toUpperCase();
  if (m.max) limpo = limpo.slice(0, m.max);
  return m.formatar ? m.formatar(limpo) : limpo;
}

/** Data digitada: dd/mm/aaaa, só números — as barras entram sozinhas. */
const MASCARA_DATA: Mascara = {
  aceita: DIGITO, max: 8, inputmode: 'numeric',
  formatar: d => d.length > 4 ? `${d.slice(0, 2)}/${d.slice(2, 4)}/${d.slice(4)}`
    : d.length > 2 ? `${d.slice(0, 2)}/${d.slice(2)}` : d
};

/**
 * Base das máscaras: filtra o que foi digitado ou colado, reaplica a pontuação e
 * devolve o cursor para o mesmo ponto. O valor corrigido é redisparado como
 * `input`, então o formulário (reativo, ngModel ou datepicker) recebe o valor
 * final — e a segunda passada não muda nada, o que encerra o ciclo.
 */
@Directive()
abstract class CampoMascarado implements AfterContentInit {
  protected readonly el = inject<ElementRef<HTMLInputElement>>(ElementRef);
  protected readonly controle = inject(NgControl, { optional: true, self: true });

  protected abstract mascara(): Mascara;

  ngAfterContentInit(): void {
    const m = this.mascara();
    const el = this.el.nativeElement;
    if (!el.hasAttribute('inputmode')) el.setAttribute('inputmode', m.inputmode);
    if (m.max && m.formatar && !el.hasAttribute('maxlength')) {
      el.setAttribute('maxlength', `${m.formatar('9'.repeat(m.max)).length}`);
    }

    const validar = m.validar;
    const c = this.controle?.control;
    if (validar && c) {
      const validador: ValidatorFn = (ctrl: AbstractControl): ValidationErrors | null => {
        const v = `${ctrl.value ?? ''}`;
        if (!v) return null;
        const erro = validar(this.limpar(v));
        return erro ? { mascara: erro } : null;
      };
      c.addValidators(validador);
      c.updateValueAndValidity({ emitEvent: false });
    }
  }

  private limpar(valor: string): string {
    const m = this.mascara();
    let limpo = [...valor].filter(ch => m.aceita(ch)).join('');
    if (m.maiusculas) limpo = limpo.toUpperCase();
    return m.max ? limpo.slice(0, m.max) : limpo;
  }

  /** Bloqueia a tecla inválida antes de ela aparecer no campo (sem "piscar"). */
  @HostListener('beforeinput', ['$event'])
  aoInserir(e: InputEvent): void {
    if (e.inputType !== 'insertText' || !e.data) return;
    if (![...e.data].every(ch => this.mascara().aceita(ch))) e.preventDefault();
  }

  @HostListener('input', ['$event'])
  aoDigitar(e: Event): void {
    // Teclados com sugestão (Gboard) compõem a palavra: mexer no valor no meio da
    // composição duplica letras. Corrige ao fim dela.
    if ((e as InputEvent).isComposing) return;
    this.aplicar();
  }

  @HostListener('compositionend')
  @HostListener('blur')
  aplicar(): void {
    const el = this.el.nativeElement;
    const bruto = el.value;
    const m = this.mascara();
    const novo = (m.formatar ?? (s => s))(this.limpar(bruto));
    if (novo === bruto) return;

    const focado = document.activeElement === el;
    const cursor = el.selectionStart ?? bruto.length;
    const aceitosAntes = [...bruto.slice(0, cursor)].filter(ch => m.aceita(ch)).length;

    el.value = novo;
    if (focado) {
      let pos = 0;
      for (let n = 0; pos < novo.length && n < aceitosAntes; pos++) {
        if (m.aceita(novo[pos])) n++;
      }
      el.setSelectionRange(pos, pos);
    }
    el.dispatchEvent(new Event('input', { bubbles: true }));
  }
}

/** `<input appMascara="cep">` — ver {@link TipoMascara}. */
@Directive({ selector: 'input[appMascara]', standalone: true })
export class MascaraDirective extends CampoMascarado {
  readonly appMascara = input.required<TipoMascara>();
  protected mascara(): Mascara { return MASCARAS[this.appMascara()]; }
}

/**
 * Todo campo de data com calendário aceita só números e monta "dd/mm/aaaa"
 * enquanto se digita. Aplicada pelo seletor: basta importar no componente.
 */
@Directive({
  selector: 'input[matDatepicker]',
  standalone: true,
  host: { autocomplete: 'off' }
})
export class DataInputDirective extends CampoMascarado {
  protected mascara(): Mascara { return MASCARA_DATA; }
}

/**
 * Campos numéricos: o `type="number"` do navegador aceita "e", "+" e, no
 * celular, sinal e vírgula onde não cabem. Aqui só passa o que o campo usa —
 * o sinal de menos só com `min` negativo (ou sem `min`, como latitude), a casa
 * decimal só quando o `step` não é inteiro (`step="any"`, `step="0.5"`).
 */
@Directive({ selector: 'input[type=number]', standalone: true })
export class NumeroInputDirective {
  private readonly el = inject<ElementRef<HTMLInputElement>>(ElementRef);

  private permitido(c: string): boolean {
    if (DIGITO(c)) return true;
    const el = this.el.nativeElement;
    if (c === '-') return !el.hasAttribute('min') || Number(el.getAttribute('min')) < 0;
    if (c === '.' || c === ',') {
      const step = el.getAttribute('step');
      return step === 'any' || (!!step && !Number.isInteger(Number(step)));
    }
    return false;
  }

  @HostListener('keydown', ['$event'])
  aoTeclar(e: KeyboardEvent): void {
    if (e.ctrlKey || e.metaKey || e.altKey || e.key.length !== 1) return;
    if (!this.permitido(e.key)) e.preventDefault();
  }

  @HostListener('beforeinput', ['$event'])
  aoInserir(e: InputEvent): void {
    if (e.inputType === 'insertText' && e.data && ![...e.data].every(c => this.permitido(c))) {
      e.preventDefault();
    }
  }

  /** A rolagem da página sobre o campo focado mudava o número sem querer. */
  @HostListener('wheel')
  aoRolar(): void {
    if (document.activeElement === this.el.nativeElement) this.el.nativeElement.blur();
  }
}

/** Tudo o que um formulário precisa importar para os campos aceitarem só o próprio tipo. */
export const CAMPOS_TIPADOS = [
  MascaraDirective, DataInputDirective, NumeroInputDirective, HoraInputDirective
] as const;
