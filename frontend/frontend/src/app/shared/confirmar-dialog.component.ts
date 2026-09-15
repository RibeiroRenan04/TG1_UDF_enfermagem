import { Component, Inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';

export interface ConfirmarDialogData {
  titulo: string;
  mensagem: string;
  /** Consequência da ação, em destaque menor abaixo da pergunta. */
  detalhe?: string;
  textoConfirmar?: string;
  icone?: string;
  /** `warn` para ações que mudam o ciclo de vida de um registro. */
  cor?: 'primary' | 'warn';
}

/**
 * Confirmação explícita em modal. Substitui o `confirm()` do navegador nas ações
 * que alteram o estado de um registro: o foco inicial fica em "Cancelar" para
 * um Enter apressado não confirmar sem querer.
 *
 * Fecha com `true` quando confirmado.
 */
@Component({
  selector: 'app-confirmar-dialog',
  standalone: true,
  imports: [CommonModule, MatDialogModule, MatButtonModule, MatIconModule],
  template: `
    <h2 mat-dialog-title class="titulo">
      <mat-icon *ngIf="data.icone" [color]="data.cor ?? 'primary'">{{ data.icone }}</mat-icon>
      <span>{{ data.titulo }}</span>
    </h2>
    <mat-dialog-content>
      <p class="mensagem">{{ data.mensagem }}</p>
      <p class="detalhe" *ngIf="data.detalhe">{{ data.detalhe }}</p>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button [mat-dialog-close]="false" cdkFocusInitial>Cancelar</button>
      <button mat-raised-button [color]="data.cor ?? 'primary'" [mat-dialog-close]="true">
        {{ data.textoConfirmar ?? 'Confirmar' }}
      </button>
    </mat-dialog-actions>
  `,
  styles: [`
    .titulo { display: flex; align-items: center; gap: 8px; }
    .mensagem { margin: 0 0 8px; font-size: 0.95rem; color: #111827; }
    .detalhe { margin: 0; font-size: 0.85rem; color: #4b5563; line-height: 1.45; }
  `]
})
export class ConfirmarDialogComponent {
  constructor(@Inject(MAT_DIALOG_DATA) public data: ConfirmarDialogData) {}
}
