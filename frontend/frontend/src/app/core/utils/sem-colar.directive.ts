import { Directive, HostListener, output } from '@angular/core';

/**
 * Obriga a digitar: bloqueia colar (Ctrl+V, Shift+Insert, menu do botão direito) e arrastar
 * texto para o campo. Usado na entrega da atividade remota, para a resposta não vir copiada.
 */
@Directive({ selector: '[appSemColar]', standalone: true })
export class SemColarDirective {
  readonly bloqueado = output<void>();

  @HostListener('paste', ['$event'])
  @HostListener('drop', ['$event'])
  aoColar(evento: Event): void {
    evento.preventDefault();
    this.bloqueado.emit();
  }

  @HostListener('beforeinput', ['$event'])
  aoInserir(evento: InputEvent): void {
    if (evento.inputType === 'insertFromPaste' || evento.inputType === 'insertFromDrop') {
      evento.preventDefault();
      this.bloqueado.emit();
    }
  }
}
