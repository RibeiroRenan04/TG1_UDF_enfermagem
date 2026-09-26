import { Component, Inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';

export interface SenhaProvisoriaDialogData {
  nome: string;
  email: string;
  senha: string;
}

/**
 * Mostra a senha provisória de um membro da equipe uma única vez: o backend não
 * a guarda em texto e não a devolve de novo. Quem recebe troca no primeiro acesso.
 */
@Component({
  selector: 'app-senha-provisoria-dialog',
  standalone: true,
  imports: [CommonModule, MatDialogModule, MatButtonModule, MatIconModule, MatTooltipModule],
  template: `
    <h2 mat-dialog-title class="titulo">
      <mat-icon color="primary">key</mat-icon>
      <span>Senha provisória gerada</span>
    </h2>
    <mat-dialog-content>
      <p class="mensagem">Repasse pessoalmente para <strong>{{ data.nome }}</strong>.</p>
      <dl class="credenciais">
        <dt>Login</dt>
        <dd>{{ data.email }}</dd>
        <dt>Senha provisória</dt>
        <dd class="senha">
          <code>{{ data.senha }}</code>
          <button mat-icon-button type="button" (click)="copiar()"
                  [matTooltip]="copiada() ? 'Copiada' : 'Copiar senha'" aria-label="Copiar senha">
            <mat-icon>{{ copiada() ? 'check' : 'content_copy' }}</mat-icon>
          </button>
        </dd>
      </dl>
      <p class="aviso">
        <mat-icon>warning</mat-icon>
        Esta senha não será exibida de novo. No primeiro acesso será obrigatório criar uma senha nova.
        Não envie por grupos ou canais compartilhados.
      </p>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-raised-button color="primary" [mat-dialog-close]="true" cdkFocusInitial>Entendi</button>
    </mat-dialog-actions>
  `,
  styles: [`
    .titulo { display: flex; align-items: center; gap: 8px; }
    .mensagem { margin: 0 0 12px; font-size: 0.95rem; color: #111827; }
    .credenciais { display: grid; grid-template-columns: auto 1fr; gap: 6px 16px; margin: 0 0 12px; align-items: center; }
    .credenciais dt { font-size: 0.8rem; color: #6b7280; }
    .credenciais dd { margin: 0; overflow-wrap: anywhere; }
    .senha { display: flex; align-items: center; gap: 4px; }
    .senha code { font-size: 1.15rem; letter-spacing: 0.08em; background: #f3f4f6; padding: 4px 8px; border-radius: 6px; }
    .aviso { display: flex; gap: 8px; align-items: flex-start; margin: 0; font-size: 0.85rem; color: #92400e;
             background: #fffbeb; padding: 8px 10px; border-radius: 6px; line-height: 1.45; }
    .aviso mat-icon { font-size: 18px; width: 18px; height: 18px; flex-shrink: 0; }
  `]
})
export class SenhaProvisoriaDialogComponent {
  copiada = signal(false);

  constructor(@Inject(MAT_DIALOG_DATA) public data: SenhaProvisoriaDialogData) {}

  copiar(): void {
    navigator.clipboard?.writeText(this.data.senha).then(() => this.copiada.set(true));
  }
}
