import { Component, Inject, OnInit, computed, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatSelectModule } from '@angular/material/select';
import { UsersService } from '../../core/services/users.service';
import { GroupsService, VinculoRecusado } from '../../core/services/groups.service';
import { StudentGroup, UserDto } from '../../core/models/models';
import { mensagemErro } from '../../core/utils/api-error';

export interface VincularAlunosDialogData {
  group: StudentGroup;
}

export interface VincularAlunosResult {
  vinculados: number;
  desvinculados: number;
}

/** Situação do aluno em relação à turma, pelo que está gravado. */
type SituacaoFiltro = 'todos' | 'nesta_turma' | 'fora_da_turma' | 'outra_turma' | 'sem_turma';

/**
 * Vincula alunos a uma turma. O vínculo (GroupMembership) é o que libera o
 * rodízio, o check-in e os relatórios do aluno — sem ele a turma não pode
 * receber alocação.
 */
@Component({
  selector: 'app-vincular-alunos-dialog',
  standalone: true,
  imports: [
    CommonModule, FormsModule,
    MatDialogModule, MatButtonModule, MatCheckboxModule,
    MatFormFieldModule, MatInputModule, MatIconModule,
    MatProgressSpinnerModule, MatTooltipModule, MatSelectModule
  ],
  template: `
    <h2 mat-dialog-title>Vincular alunos — turma {{ data.group.code }}</h2>

    <mat-dialog-content>
      <p class="hint">
        Marque os alunos que fazem parte de <strong>{{ data.group.name }}</strong>.
        O aluno pode fazer parte de mais de uma turma: marcar aqui não desfaz os vínculos
        dele com outras turmas, desde que haja compatibilidade de agenda (turnos ou dias
        da semana diferentes).
      </p>

      <div class="erro" *ngIf="erro()">
        <mat-icon>error_outline</mat-icon>
        <span>{{ erro() }}</span>
      </div>

      <!-- Alunos que a API recusou: o resto foi gravado, estes ficaram de fora. -->
      <div class="recusados" *ngIf="recusados().length">
        <div class="recusados-titulo">
          <mat-icon>block</mat-icon>
          {{ recusados().length }} aluno(s) não foram vinculados — os demais foram salvos:
        </div>
        <ul>
          <li *ngFor="let r of recusados()"><strong>{{ r.nome || 'Aluno' }}</strong>: {{ r.motivo }}</li>
        </ul>
      </div>

      <!-- A busca ocupa a linha inteira: dividindo espaço com os seletores, o
           rótulo ficava cortado pela lupa. -->
      <mat-form-field appearance="outline" class="busca" subscriptSizing="dynamic">
        <mat-icon matPrefix>search</mat-icon>
        <mat-label>Buscar por nome ou RGM</mat-label>
        <input matInput [(ngModel)]="filtro" (ngModelChange)="filtroSignal.set($event)">
      </mat-form-field>

      <!-- Com semestre e turno, "marcar todos" seleciona a turma inteira
           (ex.: os 100 alunos do 7° manhã) em dois cliques. -->
      <div class="filtros">
        <mat-form-field appearance="outline" subscriptSizing="dynamic">
          <mat-label>Semestre</mat-label>
          <mat-select [ngModel]="filtroSemestre()" (ngModelChange)="filtroSemestre.set($event)">
            <mat-option [value]="null">Todos</mat-option>
            <mat-option [value]="7">7° semestre</mat-option>
            <mat-option [value]="8">8° semestre</mat-option>
          </mat-select>
        </mat-form-field>
        <mat-form-field appearance="outline" subscriptSizing="dynamic">
          <mat-label>Turno</mat-label>
          <mat-select [ngModel]="filtroTurno()" (ngModelChange)="filtroTurno.set($event)">
            <mat-option [value]="null">Todos</mat-option>
            <mat-option value="manha">Manhã</mat-option>
            <mat-option value="tarde">Tarde</mat-option>
            <mat-option value="noite">Noite</mat-option>
          </mat-select>
        </mat-form-field>
        <mat-form-field appearance="outline" subscriptSizing="dynamic">
          <mat-label>Situação</mat-label>
          <mat-select [ngModel]="filtroSituacao()" (ngModelChange)="filtroSituacao.set($event)">
            <mat-option value="todos">Todos</mat-option>
            <mat-option value="nesta_turma">Já nesta turma</mat-option>
            <mat-option value="fora_da_turma">Fora desta turma</mat-option>
            <mat-option value="outra_turma">Em outra turma</mat-option>
            <mat-option value="sem_turma">Sem nenhuma turma</mat-option>
          </mat-select>
        </mat-form-field>
        <button mat-icon-button type="button" class="limpar" *ngIf="temFiltroAtivo()"
                matTooltip="Limpar filtros" aria-label="Limpar filtros" (click)="limparFiltros()">
          <mat-icon>filter_alt_off</mat-icon>
        </button>
      </div>

      <div *ngIf="loading()" class="center"><mat-spinner diameter="36"></mat-spinner></div>

      <ng-container *ngIf="!loading()">
        <div class="toolbar-row">
          <!-- Checkbox mestre: marca/desmarca todos os alunos exibidos pelos filtros. -->
          <mat-checkbox [checked]="todosExibidosMarcados()"
                        [indeterminate]="algunsExibidosMarcados()"
                        [disabled]="!alunosFiltrados().length"
                        (change)="alternarExibidos($event.checked)">
            Marcar todos os exibidos ({{ alunosFiltrados().length }})
          </mat-checkbox>
          <span class="count">
            {{ selecionados().size }} na turma
            <ng-container *ngIf="adicionados().length"> · <span class="mais">+{{ adicionados().length }}</span></ng-container>
            <ng-container *ngIf="removidos().length"> · <span class="menos">−{{ removidos().length }}</span></ng-container>
          </span>
        </div>

        <div class="lista" *ngIf="alunosFiltrados().length; else vazio">
          <label class="linha" *ngFor="let a of alunosFiltrados()">
            <mat-checkbox [checked]="selecionados().has(a.id)" (change)="alternar(a.id)"></mat-checkbox>
            <span class="dados">
              <span class="nome">{{ a.fullName }}</span>
              <span class="meta">
                RGM {{ a.rgm || '—' }}
                <ng-container *ngIf="a.semester"> · {{ a.semester }}° sem.</ng-container>
                <ng-container *ngIf="a.shift"> · {{ turnoLabel(a.shift) }}</ng-container>
              </span>
            </span>
            <span class="outra-turma" *ngIf="outrasTurmas(a) as outras"
                  [matTooltip]="'O vínculo com ' + outras + ' é mantido'">
              <mat-icon>groups</mat-icon> {{ outras }}
            </span>
          </label>
        </div>

        <ng-template #vazio>
          <div class="center muted">
            <p *ngIf="alunos().length">Nenhum aluno corresponde aos filtros.</p>
            <p *ngIf="!alunos().length">
              Nenhum aluno ativo cadastrado. Importe a lista de alunos antes de montar a turma.
            </p>
          </div>
        </ng-template>
      </ng-container>
    </mat-dialog-content>

    <mat-dialog-actions align="end">
      <button mat-button (click)="fechar()">{{ recusados().length ? 'Fechar' : 'Cancelar' }}</button>
      <button mat-raised-button color="primary"
              [disabled]="loading() || saving() || !temAlteracoes()"
              (click)="salvar()">
        <mat-spinner *ngIf="saving()" diameter="18" class="btn-spinner"></mat-spinner>
        <span>Salvar vínculos</span>
      </button>
    </mat-dialog-actions>
  `,
  styles: [`
    .hint { font-size: 0.85rem; color: #6B7280; margin: 0 0 12px; }
    .busca {
      width: 100%; margin-bottom: 8px;
      mat-icon[matPrefix] { margin: 0 4px 0 10px; color: #6B7280; }
    }
    .filtros {
      display: flex; gap: 8px; flex-wrap: wrap; align-items: center; margin-bottom: 8px;
      mat-form-field { flex: 1 1 150px; min-width: 0; }
      .limpar { flex-shrink: 0; }
    }
    .center { display: flex; justify-content: center; padding: 24px; }
    .muted { color: #6B7280; font-size: 0.85rem; text-align: center; }
    .toolbar-row {
      display: flex; align-items: center; gap: 8px; flex-wrap: wrap;
      margin-bottom: 6px; padding: 0 2px;
      mat-checkbox { font-size: 0.82rem; }
      .count { margin-left: auto; font-size: 0.78rem; color: #6B7280; }
      .mais { color: #166534; font-weight: 600; }
      .menos { color: #991b1b; font-weight: 600; }
    }
    .recusados {
      font-size: 0.78rem; color: #92400e; background: #fffbeb;
      border: 1px solid #fde68a; border-radius: 8px;
      padding: 8px 10px; margin-bottom: 12px;
      .recusados-titulo { display: flex; align-items: center; gap: 6px; font-weight: 600; }
      mat-icon { font-size: 18px; width: 18px; height: 18px; }
      ul { margin: 6px 0 0; padding-left: 20px; max-height: 120px; overflow-y: auto; }
    }
    .lista { max-height: 320px; overflow-y: auto; border: 1px solid #e5e7eb; border-radius: 8px; }
    .linha {
      display: flex; align-items: center; gap: 10px;
      padding: 6px 10px; cursor: pointer;
      border-bottom: 1px solid #f3f4f6;
      &:last-child { border-bottom: 0; }
      &:hover { background: #f9fafb; }
    }
    .dados { flex: 1; min-width: 0; display: flex; flex-direction: column; }
    /* Cor explícita: sem ela o nome herdava o cinza secundário do conteúdo do modal. */
    .nome { font-size: 0.86rem; font-weight: 500; color: #111827; }
    .meta { font-size: 0.72rem; color: #6B7280; }
    .outra-turma {
      display: inline-flex; align-items: center; gap: 3px;
      font-size: 0.7rem; color: #0B427A; background: #e0f2fe;
      padding: 2px 8px; border-radius: 9999px; flex-shrink: 0;
      mat-icon { font-size: 14px; width: 14px; height: 14px; }
    }
    .erro {
      display: flex; align-items: flex-start; gap: 8px;
      font-size: 0.8rem; color: #991b1b; background: #fef2f2;
      border: 1px solid #fecaca; border-radius: 8px;
      padding: 8px 10px; margin-bottom: 12px;
      mat-icon { font-size: 18px; width: 18px; height: 18px; flex-shrink: 0; }
    }
    .btn-spinner { display: inline-block; margin-right: 8px; }
  `]
})
export class VincularAlunosDialogComponent implements OnInit {
  alunos = signal<UserDto[]>([]);
  selecionados = signal<Set<string>>(new Set());
  loading = signal(true);
  saving = signal(false);
  /** Recusa da API — hoje, a agenda incompatível entre dois rodízios do aluno. */
  erro = signal<string | null>(null);

  filtro = '';
  filtroSignal = signal('');
  filtroSemestre = signal<number | null>(null);
  filtroTurno = signal<string | null>(null);
  filtroSituacao = signal<SituacaoFiltro>('todos');

  /** Alunos que a API recusou no último salvamento, com o motivo. */
  recusados = signal<VinculoRecusado[]>([]);

  /**
   * Vínculos gravados no servidor. Servem para calcular o que mudou e para o
   * filtro de situação — que olha o que está salvo, e não a marcação em
   * andamento, para a lista não "pular" a cada clique no checkbox.
   */
  private originais = signal<Set<string>>(new Set());

  /** O que já foi gravado neste modal, quando um salvamento teve recusados. */
  private salvo?: VincularAlunosResult;

  alunosFiltrados = computed(() => {
    const termo = this.filtroSignal().trim().toLowerCase();
    const semestre = this.filtroSemestre();
    const turno = this.filtroTurno();
    const situacao = this.filtroSituacao();
    const nestaTurma = this.originais();
    return this.alunos().filter(a =>
      (!termo || a.fullName.toLowerCase().includes(termo) || (a.rgm ?? '').toLowerCase().includes(termo))
      && (semestre == null || a.semester === semestre)
      && (turno == null || a.shift === turno)
      && this.atendeSituacao(a, situacao, nestaTurma)
    );
  });

  /** Algum filtro diferente do padrão — mostra o botão de limpar. */
  temFiltroAtivo = computed(() =>
    !!this.filtroSignal().trim() || this.filtroSemestre() != null
    || this.filtroTurno() != null || this.filtroSituacao() !== 'todos');

  limparFiltros(): void {
    this.filtro = '';
    this.filtroSignal.set('');
    this.filtroSemestre.set(null);
    this.filtroTurno.set(null);
    this.filtroSituacao.set('todos');
  }

  private atendeSituacao(a: UserDto, situacao: SituacaoFiltro, nestaTurma: Set<string>): boolean {
    switch (situacao) {
      case 'nesta_turma': return nestaTurma.has(a.id);
      case 'fora_da_turma': return !nestaTurma.has(a.id);
      case 'outra_turma': return !!this.outrasTurmas(a);
      case 'sem_turma': return this.turmasDe(a).length === 0;
      default: return true;
    }
  }

  constructor(
    public dialogRef: MatDialogRef<VincularAlunosDialogComponent>,
    private usersService: UsersService,
    private groupsService: GroupsService,
    @Inject(MAT_DIALOG_DATA) public data: VincularAlunosDialogData
  ) {}

  ngOnInit(): void {
    this.usersService.getAll().subscribe({
      next: (todos) => {
        const ativos = todos.filter(u => u.role === 'aluno' && u.isActive !== false);
        this.alunos.set(ativos);
        this.originais.set(new Set(ativos.filter(a => this.turmasDe(a).includes(this.data.group.id)).map(a => a.id)));
        this.selecionados.set(new Set(this.originais()));
        this.loading.set(false);
      },
      error: () => this.loading.set(false)
    });
  }

  /**
   * Ids das turmas do aluno. `groups` é a lista completa; `groupId` cobre a
   * resposta antiga, de quando o aluno só podia ter uma turma.
   */
  private turmasDe(a: UserDto): string[] {
    if (a.groups?.length) return a.groups.map(g => g.id);
    return a.groupId ? [a.groupId] : [];
  }

  /**
   * Demais turmas do aluno, só para informar: elas continuam valendo depois de
   * vincular a esta.
   */
  outrasTurmas(a: UserDto): string {
    const outras = (a.groups ?? [])
      .filter(g => g.id !== this.data.group.id)
      .map(g => g.code || g.name);

    if (!outras.length && a.groupId && a.groupId !== this.data.group.id) {
      return a.groupCode || a.groupName || '';
    }
    return outras.join(', ');
  }

  alternar(id: string): void {
    this.selecionados.update(atual => {
      const proximo = new Set(atual);
      proximo.has(id) ? proximo.delete(id) : proximo.add(id);
      return proximo;
    });
  }

  // ── Seleção em massa ──────────────────────────────────────────────────────
  /** Todos os exibidos pelos filtros estão marcados. */
  todosExibidosMarcados = computed(() => {
    const exibidos = this.alunosFiltrados();
    const marcados = this.selecionados();
    return exibidos.length > 0 && exibidos.every(a => marcados.has(a.id));
  });

  /** Parte dos exibidos marcada — o checkbox mestre fica no estado intermediário. */
  algunsExibidosMarcados = computed(() => {
    const marcados = this.selecionados();
    const qtd = this.alunosFiltrados().filter(a => marcados.has(a.id)).length;
    return qtd > 0 && !this.todosExibidosMarcados();
  });

  /** Checkbox mestre: marca ou desmarca de uma vez todos os alunos exibidos. */
  alternarExibidos(marcar: boolean): void {
    const ids = this.alunosFiltrados().map(a => a.id);
    this.selecionados.update(atual => {
      const proximo = new Set(atual);
      ids.forEach(id => marcar ? proximo.add(id) : proximo.delete(id));
      return proximo;
    });
  }

  temAlteracoes(): boolean {
    return this.adicionados().length > 0 || this.removidos().length > 0;
  }

  adicionados(): string[] {
    return [...this.selecionados()].filter(id => !this.originais().has(id));
  }

  removidos(): string[] {
    return [...this.originais()].filter(id => !this.selecionados().has(id));
  }

  /**
   * Grava tudo numa chamada só. Antes era uma requisição por aluno: 100 alunos
   * eram 100 chamadas, e a primeira recusa interrompia o resto no meio.
   * Com recusados, o modal fica aberto listando cada um com o motivo — o que
   * era válido já foi salvo.
   */
  salvar(): void {
    const paraVincular = this.adicionados();
    const paraDesvincular = this.removidos();
    if (!paraVincular.length && !paraDesvincular.length) return;

    this.saving.set(true);
    this.erro.set(null);
    this.recusados.set([]);

    this.groupsService.atualizarMembros(this.data.group.id, paraVincular, paraDesvincular).subscribe({
      next: (res) => {
        this.saving.set(false);
        const acumulado = {
          vinculados: (this.salvo?.vinculados ?? 0) + res.vinculados,
          desvinculados: (this.salvo?.desvinculados ?? 0) + res.desvinculados
        };

        if (!res.recusados.length) {
          this.dialogRef.close(acumulado as VincularAlunosResult);
          return;
        }

        // O servidor gravou o resto: a tela passa a refletir isso, e os
        // recusados voltam a aparecer desmarcados.
        this.salvo = acumulado;
        const recusadosIds = new Set(res.recusados.map(r => r.studentId));
        const agora = new Set([...this.selecionados()].filter(id => !recusadosIds.has(id)));
        this.originais.set(new Set(agora));
        this.selecionados.set(agora);
        this.recusados.set(res.recusados);
      },
      error: (err) => {
        this.saving.set(false);
        this.erro.set(mensagemErro(err, 'Não foi possível salvar os vínculos.'));
      }
    });
  }

  /** Fechar depois de um salvamento parcial ainda avisa a tela para recarregar. */
  fechar(): void {
    this.dialogRef.close(this.salvo ?? null);
  }

  turnoLabel(shift: string): string {
    return { manha: 'Manhã', tarde: 'Tarde', noite: 'Noite' }[shift] ?? shift;
  }
}
