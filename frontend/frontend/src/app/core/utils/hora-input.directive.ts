import { Directive, ElementRef, HostListener, OnInit, inject } from '@angular/core';
import { NgControl, Validators } from '@angular/forms';

/** "HH:mm" de 00:00 a 23:59. */
export const HORA_24H = /^([01]\d|2[0-3]):[0-5]\d$/;

/**
 * Hora em 24h com máscara "HH:mm". O campo nativo type="time" segue o idioma do navegador e
 * mostrava AM/PM, o que levava a cadastrar 20:00 achando que era 8h.
 */
@Directive({
  selector: 'input[appHora]',
  standalone: true,
  host: { inputmode: 'numeric', maxlength: '5', placeholder: 'HH:mm', autocomplete: 'off' }
})
export class HoraInputDirective implements OnInit {
  private readonly el = inject<ElementRef<HTMLInputElement>>(ElementRef);
  private readonly controle = inject(NgControl, { optional: true, self: true });

  ngOnInit(): void {
    this.controle?.control?.addValidators(Validators.pattern(HORA_24H));
  }

  @HostListener('input')
  aoDigitar(): void {
    const digitos = this.el.nativeElement.value.replace(/\D/g, '').slice(0, 4);
    const valor = digitos.length > 2 ? `${digitos.slice(0, 2)}:${digitos.slice(2)}` : digitos;
    this.el.nativeElement.value = valor;
    this.controle?.control?.setValue(valor, { emitEvent: true });
  }
}
