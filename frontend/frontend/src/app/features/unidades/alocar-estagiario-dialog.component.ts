import { Component, Inject, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';
import { UnidadesSaudeService } from '../../core/services/unidades-saude.service';
import { EstagiarioDisponivel, UnidadeSaude } from '../../core/models/models';

/**
 * Busca e alocação de um estagiário. Só aparecem usuários com perfil de aluno —
 * e a API confirma isso de novo, não confiando na tela.
 */
@Component({
  selector: 'app-alocar-estagiario-dialog',
  standalone: true,
  imports: [
    CommonModule, FormsModule,
    MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule,
    MatIconModule, MatProgressSpinnerModule, MatTooltipModule
  ],
  template: `
    <h2 mat-dialog-title>Alocar estagiário em {{ data.unidade.nome }}</h2>

    <mat-dialog-content>
      <mat-form-field appearance="outline" class="busca" subscriptSizing="dynamic">
        <mat-label>Buscar por nome ou RGM</mat-label>
        <input matInput [(ngModel)]="termo" (ngModelChange)="buscar()" autocomplete="off">
        <mat-icon matPrefix>search</mat-icon>
      </mat-form-field>

      <div *ngIf="carregando()" class="carregando"><mat-spinner diameter="32"></mat-spinner></div>

      <div class="lista" *ngIf="!carregando()">
        <div class="vazio" *ngIf="!alunos().length">
          <mat-icon>person_search</mat-icon>
          <p>Nenhum aluno encontrado{{ termo ? ' para “' + termo + '”' : '' }}.</p>
        </div>

        <div class="aluno" *ngFor="let a of alunos()">
          <span class="avatar">{{ a.nome.charAt(0).toUpperCase() }}</span>
          <span class="dados">
            <span class="nome">{{ a.nome }}</span>
            <span class="meta">
              RGM {{ a.rgm || '—' }}
              <ng-container *ngIf="a.semestre"> · {{ a.semestre }}° sem</ng-container>
              <ng-container *ngIf="a.turno"> · {{ turnoLabel(a.turno) }}</ng-container>
              <ng-container *ngIf="a.turma"> · turma {{ a.turma }}</ng-container>
            </span>
            <span class="email">{{ a.email || '—' }}</span>
            <!-- Alocar quem já tem unidade é uma transferência: precisa ser explícito. -->
            <span class="ja-alocado" *ngIf="a.unidadeAtualId && a.unidadeAtualId !== data.unidade.id">
              <mat-icon>info</mat-icon>
              Já alocado(a) em <strong>{{ a.unidadeAtualNome }}</strong>
            </span>
            <span class="aqui" *ngIf="a.unidadeAtualId === data.unidade.id">
              <mat-icon>check_circle</mat-icon> Já está nesta unidade
            </span>
          </span>

          <button mat-flat-button color="primary"
                  *ngIf="a.unidadeAtualId !== data.unidade.id"
                  [disabled]="salvandoId() === a.id"
                  (click)="alocar(a)">
            <mat-spinner *ngIf="salvandoId() === a.id" diameter="16"
                         style="display:inline-block;margin-right:6px"></mat-spinner>
            <span *ngIf="salvandoId() !== a.id">
              {{ a.unidadeAtualId ? 'Transferir' : 'Alocar' }}
            </span>
          </button>
        </div>
      </div>

      <p class="erro" *ngIf="erro()">{{ erro() }}</p>
    </mat-dialog-content>

    <mat-dialog-actions align="end">
      <button mat-button (click)="dialogRef.close(alocouAlgum)">Fechar</button>
    </mat-dialog-actions>
  `,
  styles: [`
    .busca { width: 100%; min-width: 480px; margin-bottom: 14px; }
    .carregando { display: flex; justify-content: center; padding: 28px; }
    .vazio {
      text-align: center; color: #6B7280; padding: 28px 8px;
      mat-icon { font-size: 38px; width: 38px; height: 38px; color: #d1d5db; }
      p { font-size: 0.85rem; margin: 8px 0 0; }
    }
    .lista { display: flex; flex-direction: column; max-height: 46vh; overflow-y: auto; }
    .aluno {
      display: flex; align-items: center; gap: 10px;
      padding: 10px 4px; border-bottom: 1px solid #f3f4f6;
      &:last-child { border-bottom: none; }
    }
    .avatar {
      width: 34px; height: 34px; border-radius: 50%; flex-shrink: 0;
      background: #0056A6; color: #fff;
      display: flex; align-items: center; justify-content: center;
      font-weight: 600; font-size: 0.85rem;
    }
    .dados { flex: 1; display: flex; flex-direction: column; min-width: 0; }
    .nome { font-size: 0.9rem; color: #111827; font-weight: 500; }
    .meta, .email { font-size: 0.74rem; color: #6B7280; }
    .ja-alocado, .aqui {
      display: inline-flex; align-items: center; gap: 4px;
      font-size: 0.72rem; margin-top: 3px;
      mat-icon { font-size: 14px; width: 14px; height: 14px; }
    }
    .ja-alocado { color: #92400e; }
    .aqui { color: #166534; }
    .erro { color: #b91c1c; font-size: 0.85rem; margin: 10px 0 0; }
  `]
})
export class AlocarEstagiarioDialogComponent implements OnInit {
  alunos = signal<EstagiarioDisponivel[]>([]);
  carregando = signal(true);
  salvandoId = signal<string | null>(null);
  erro = signal('');
  termo = '';
  alocouAlgum = false;

  private debounce?: ReturnType<typeof setTimeout>;

  constructor(
    public dialogRef: MatDialogRef<AlocarEstagiarioDialogComponent>,
    private service: UnidadesSaudeService,
    @Inject(MAT_DIALOG_DATA) public data: { unidade: UnidadeSaude }
  ) {}

  ngOnInit(): void { this.carregar(); }

  buscar(): void {
    clearTimeout(this.debounce);
    this.debounce = setTimeout(() => this.carregar(), 300);
  }

  private carregar(): void {
    this.carregando.set(true);
    this.service.getEstagiariosDisponiveis(this.data.unidade.id, this.termo || undefined).subscribe({
      next: (a) => { this.alunos.set(a); this.carregando.set(false); },
      error: () => { this.carregando.set(false); this.erro.set('Erro ao buscar os alunos.'); }
    });
  }

  turnoLabel(t: string): string {
    return ({ manha: 'Manhã', tarde: 'Tarde', noite: 'Noite' } as Record<string, string>)[t] ?? t;
  }

  alocar(a: EstagiarioDisponivel): void {
    // Transferir encerra a alocação anterior; confirmamos antes de fazer isso.
    if (a.unidadeAtualId &&
        !confirm(`${a.nome} está alocado(a) em "${a.unidadeAtualNome}". ` +
                 `Encerrar essa alocação e transferir para "${this.data.unidade.nome}"?`)) return;

    this.salvandoId.set(a.id);
    this.erro.set('');

    this.service.alocar(this.data.unidade.id, a.id, {
      encerrarAlocacaoAtual: !!a.unidadeAtualId
    }).subscribe({
      next: () => {
        this.salvandoId.set(null);
        this.alocouAlgum = true;
        this.carregar();
      },
      error: (err) => {
        this.salvandoId.set(null);
        this.erro.set(err?.error?.message ?? 'Erro ao alocar o estagiário.');
      }
    });
  }
}
