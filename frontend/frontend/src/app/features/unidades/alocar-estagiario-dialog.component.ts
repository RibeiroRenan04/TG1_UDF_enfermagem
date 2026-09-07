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
import { MatSelectModule } from '@angular/material/select';
import { UnidadesSaudeService } from '../../core/services/unidades-saude.service';
import { EstagiarioDisponivel, Turno, UnidadeSaude } from '../../core/models/models';

/**
 * Busca e alocação de um estagiário. Só aparecem usuários com perfil de aluno —
 * e a API confirma isso de novo, não confiando na tela.
 *
 * A alocação é por turno: o mesmo aluno pode estagiar de manhã em uma unidade e
 * à tarde em outra. Os turnos já ocupados aparecem desabilitados na lista, e a
 * API recusa a duplicidade de qualquer forma.
 */
@Component({
  selector: 'app-alocar-estagiario-dialog',
  standalone: true,
  imports: [
    CommonModule, FormsModule,
    MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule,
    MatIconModule, MatProgressSpinnerModule, MatTooltipModule, MatSelectModule
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

            <!-- Alocações já ativas, turno a turno. -->
            <span class="turnos-ativos" *ngIf="a.alocacoesAtivas?.length">
              <span class="turno-chip" *ngFor="let al of a.alocacoesAtivas"
                    [class.aqui]="al.unidadeId === data.unidade.id"
                    [matTooltip]="al.unidadeNome">
                <mat-icon>{{ al.unidadeId === data.unidade.id ? 'check_circle' : 'schedule' }}</mat-icon>
                {{ turnoLabel(al.turno) }} · {{ al.unidadeNome }}
              </span>
            </span>

            <span class="sem-turno" *ngIf="!turnosLivres(a).length && !podeTransferir(a)">
              <mat-icon>block</mat-icon>
              Todos os turnos já estão alocados nesta unidade.
            </span>
          </span>

          <span class="acao">
            <mat-form-field appearance="outline" subscriptSizing="dynamic" class="turno-select">
              <mat-label>Turno</mat-label>
              <mat-select [ngModel]="turnoEscolhido(a)" (ngModelChange)="definirTurno(a, $event)">
                <mat-option *ngFor="let t of turnos" [value]="t"
                            [disabled]="turnoOcupadoNestaUnidade(a, t)">
                  {{ turnoLabel(t) }}
                  <span class="opcao-nota" *ngIf="ocupacao(a, t) as u">— {{ u }}</span>
                </mat-option>
              </mat-select>
            </mat-form-field>

            <button mat-flat-button color="primary"
                    [disabled]="salvandoId() === a.id || !turnoEscolhido(a)"
                    (click)="alocar(a)">
              <mat-spinner *ngIf="salvandoId() === a.id" diameter="16"
                           style="display:inline-block;margin-right:6px"></mat-spinner>
              <span *ngIf="salvandoId() !== a.id">
                {{ rotuloBotao(a) }}
              </span>
            </button>
          </span>
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
    .turnos-ativos { display: flex; flex-wrap: wrap; gap: 4px; margin-top: 4px; }
    .turno-chip {
      display: inline-flex; align-items: center; gap: 3px;
      font-size: 0.68rem; padding: 2px 8px; border-radius: 999px;
      background: #fffbeb; border: 1px solid #fde68a; color: #92400e;
      max-width: 220px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap;
      mat-icon { font-size: 13px; width: 13px; height: 13px; }
      &.aqui { background: #f0fdf4; border-color: #bbf7d0; color: #166534; }
    }
    .sem-turno {
      display: inline-flex; align-items: center; gap: 4px;
      font-size: 0.7rem; color: #6B7280; margin-top: 4px;
      mat-icon { font-size: 14px; width: 14px; height: 14px; }
    }
    .acao { display: flex; align-items: center; gap: 8px; flex-shrink: 0; }
    .turno-select { width: 116px; }
    .opcao-nota { color: #9ca3af; font-size: 0.72rem; }
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

  readonly turnos: Turno[] = ['manha', 'tarde', 'noite'];

  /** Turno escolhido por aluno na lista, antes de confirmar a alocação. */
  private turnosSelecionados: Record<string, Turno> = {};

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
      next: (a) => {
        this.alunos.set(a);
        this.turnosSelecionados = {};
        this.carregando.set(false);
      },
      error: () => { this.carregando.set(false); this.erro.set('Erro ao buscar os alunos.'); }
    });
  }

  turnoLabel(t: string): string {
    return ({ manha: 'Manhã', tarde: 'Tarde', noite: 'Noite' } as Record<string, string>)[t] ?? t;
  }

  /** Turnos ainda livres para o aluno (sem alocação ativa em nenhuma unidade). */
  turnosLivres(a: EstagiarioDisponivel): Turno[] {
    return a.turnosDisponiveis ?? [];
  }

  /** Alocação ativa do aluno naquele turno, se houver. */
  private alocacaoNoTurno(a: EstagiarioDisponivel, t: Turno) {
    return (a.alocacoesAtivas ?? []).find(x => x.turno === t);
  }

  /** Texto curto de ocupação do turno, para a opção do seletor. */
  ocupacao(a: EstagiarioDisponivel, t: Turno): string | null {
    const atual = this.alocacaoNoTurno(a, t);
    if (!atual) return null;
    return atual.unidadeId === this.data.unidade.id ? 'já nesta unidade' : atual.unidadeNome;
  }

  /** Turno ocupado por esta mesma unidade: não há nada a fazer nele. */
  turnoOcupadoNestaUnidade(a: EstagiarioDisponivel, t: Turno): boolean {
    return this.alocacaoNoTurno(a, t)?.unidadeId === this.data.unidade.id;
  }

  /** O aluno tem algum turno alocado em OUTRA unidade — dá para transferir. */
  podeTransferir(a: EstagiarioDisponivel): boolean {
    return (a.alocacoesAtivas ?? []).some(x => x.unidadeId !== this.data.unidade.id);
  }

  /**
   * Turno pré-selecionado: o primeiro livre; se não houver, o primeiro alocado em
   * outra unidade (uma transferência).
   */
  turnoEscolhido(a: EstagiarioDisponivel): Turno | null {
    const escolhido = this.turnosSelecionados[a.id];
    if (escolhido) return escolhido;

    const livre = this.turnosLivres(a)[0];
    if (livre) return livre;

    return (a.alocacoesAtivas ?? []).find(x => x.unidadeId !== this.data.unidade.id)?.turno ?? null;
  }

  definirTurno(a: EstagiarioDisponivel, t: Turno): void {
    this.turnosSelecionados[a.id] = t;
  }

  rotuloBotao(a: EstagiarioDisponivel): string {
    const t = this.turnoEscolhido(a);
    if (!t) return 'Alocar';
    return this.alocacaoNoTurno(a, t) ? 'Transferir' : 'Alocar';
  }

  alocar(a: EstagiarioDisponivel): void {
    const turno = this.turnoEscolhido(a);
    if (!turno) {
      this.erro.set(`${a.nome} já tem alocação ativa em todos os turnos nesta unidade.`);
      return;
    }

    // Transferir encerra a alocação daquele turno; as dos outros turnos continuam
    // valendo. Confirmamos antes de encerrar.
    const atual = this.alocacaoNoTurno(a, turno);
    if (atual &&
        !confirm(`${a.nome} está alocado(a) em "${atual.unidadeNome}" no turno da ` +
                 `${this.turnoLabel(turno).toLowerCase()}. Encerrar essa alocação e transferir ` +
                 `para "${this.data.unidade.nome}"? As alocações dos outros turnos continuam.`)) return;

    this.salvandoId.set(a.id);
    this.erro.set('');

    this.service.alocar(this.data.unidade.id, a.id, {
      turno,
      encerrarAlocacaoAtual: !!atual
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
