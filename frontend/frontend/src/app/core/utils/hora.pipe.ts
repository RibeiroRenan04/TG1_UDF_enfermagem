import { Pipe, PipeTransform } from '@angular/core';

/**
 * Horário "HH:mm" a partir do TimeOnly da API, que chega com os segundos
 * ("08:00:00" → "08:00").
 */
@Pipe({ name: 'hora', standalone: true })
export class HoraPipe implements PipeTransform {
  transform(valor?: string | null): string {
    return valor ? valor.slice(0, 5) : '';
  }
}
