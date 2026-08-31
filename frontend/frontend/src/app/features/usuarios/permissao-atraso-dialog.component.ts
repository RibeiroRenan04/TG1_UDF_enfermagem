import { Component, Inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { UsersService } from '../../core/services/users.service';
import { UserDto } from '../../core/models/models';

/**
 * Autorização prévia de atraso para um aluno. O professor concede a permissão e
 * registra o motivo; a carga horária do dia continua sendo exigida.
 */
@Component({
  selector: 'app-permissao-atraso-dialog',
  standalone: true,
  imports: [
    CommonModule, FormsModule,
    MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule,
    MatSlideToggleModule, MatIconModule, MatProgressSpinnerModule
  ],
  template: `
    <h2 mat-dialog-title>Permissão de atraso</h2>

    <mat-dialog-content>
      <div class="aluno-box">
        <div class="avatar">{{ data.aluno.fullName.charAt(0).toUpperCase() }}</div>
        <div>
          <div class="nome">{{ data.aluno.fullName }}</div>
          <div class="meta">
            RGM {{ data.aluno.rgm || '—' }}
            <span *ngIf="data.aluno.semester"> · {{ data.aluno.semester }}° semestre</span>
            <span *ngIf="data.aluno.shift"> · {{ turnoLabel(data.aluno.shift) }}</span>
          </div>
        </div>
      </div>

      <div class="regra-box">
        <mat-icon>schedule</mat-icon>
        <div>
          Autoriza este aluno a <strong>chegar após o horário previsto</strong> de início do
          estágio, sem que o registro seja tratado como irregularidade de horário.
          <strong>A carga horária do dia continua sendo exigida</strong> — o aluno deve
          permanecer o tempo previsto e registrar a saída normalmente.
        </div>
      </div>

      <mat-slide-toggle [(ngModel)]="permitir" class="toggle">
        Permitir atraso previamente autorizado
      </mat-slide-toggle>

      <mat-form-field appearance="outline" class="campo" *ngIf="permitir">
        <mat-label>Motivo da autorização</mat-label>
        <textarea matInput rows="3" [(ngModel)]="motivo" maxlength="500"
                  placeholder="Ex.: aluno trabalha no turno da manhã, autorizado pela coordenação a chegar às 14h."></textarea>
        <mat-hint align="end">{{ motivo.length }}/500</mat-hint>
      </mat-form-field>

      <p class="erro" *ngIf="erro()">{{ erro() }}</p>
    </mat-dialog-content>

    <mat-dialog-actions align="end">
      <button mat-button (click)="dialogRef.close(null)">Cancelar</button>
      <button mat-raised-button color="primary" [disabled]="busy()" (click)="salvar()">
        <mat-spinner *ngIf="busy()" diameter="18" style="display:inline-block;margin-right:8px"></mat-spinner>
        <span *ngIf="!busy()">Salvar</span>
      </button>
    </mat-dialog-actions>
  `,
  styles: [`
    .aluno-box {
      display: flex; gap: 12px; align-items: center; margin-bottom: 14px;
      .avatar {
        width: 40px; height: 40px; border-radius: 50%;
        background: #0056A6; color: #fff;
        display: flex; align-items: center; justify-content: center;
        font-weight: 600; flex-shrink: 0;
      }
      .nome { font-weight: 600; color: #111827; }
      .meta { font-size: 0.78rem; color: #6B7280; }
    }
    .regra-box {
      display: flex; gap: 8px; align-items: flex-start;
      background: #fffbeb; border: 1px solid #fde68a; color: #92400e;
      border-radius: 8px; padding: 10px 12px; margin-bottom: 16px;
      font-size: 0.78rem; line-height: 1.5;
      mat-icon { font-size: 20px; width: 20px; height: 20px; flex-shrink: 0; }
    }
    .toggle { display: block; margin-bottom: 14px; }
    .campo { width: 100%; min-width: 380px; }
    .erro { color: #b91c1c; font-size: 0.85rem; margin: 8px 0 0; }
  `]
})
export class PermissaoAtrasoDialogComponent {
  permitir: boolean;
  motivo: string;
  busy = signal(false);
  erro = signal('');

  constructor(
    public dialogRef: MatDialogRef<PermissaoAtrasoDialogComponent>,
    private usersService: UsersService,
    @Inject(MAT_DIALOG_DATA) public data: { aluno: UserDto }
  ) {
    this.permitir = data.aluno.allowLateArrival ?? false;
    this.motivo = data.aluno.lateArrivalNote ?? '';
  }

  turnoLabel(shift: string): string {
    return ({ manha: 'Manhã', tarde: 'Tarde', noite: 'Noite' } as Record<string, string>)[shift] ?? shift;
  }

  salvar(): void {
    this.busy.set(true);
    this.erro.set('');
    this.usersService.setLatePermission(
      this.data.aluno.id, this.permitir, this.permitir ? this.motivo.trim() : undefined
    ).subscribe({
      next: (atualizado) => { this.busy.set(false); this.dialogRef.close(atualizado); },
      error: (err) => {
        this.busy.set(false);
        this.erro.set(err?.error?.message ?? 'Erro ao salvar a permissão de atraso.');
      }
    });
  }
}
