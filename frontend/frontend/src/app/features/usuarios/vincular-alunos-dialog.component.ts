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
import { forkJoin, of } from 'rxjs';
import { UsersService } from '../../core/services/users.service';
import { StudentGroup, UserDto } from '../../core/models/models';

export interface VincularAlunosDialogData {
  group: StudentGroup;
}

export interface VincularAlunosResult {
  vinculados: number;
  desvinculados: number;
}

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
    MatProgressSpinnerModule, MatTooltipModule
  ],
  template: `
    <h2 mat-dialog-title>Vincular alunos — turma {{ data.group.code }}</h2>

    <mat-dialog-content>
      <p class="hint">
        Marque os alunos que fazem parte de <strong>{{ data.group.name }}</strong>.
        Cada aluno pertence a uma única turma: ao marcar aqui, o vínculo anterior é substituído.
      </p>

      <mat-form-field appearance="outline" class="search">
        <mat-label>Buscar por nome ou RGM</mat-label>
        <input matInput [(ngModel)]="filtro" (ngModelChange)="filtroSignal.set($event)">
        <mat-icon matSuffix>search</mat-icon>
      </mat-form-field>

      <div *ngIf="loading()" class="center"><mat-spinner diameter="36"></mat-spinner></div>

      <ng-container *ngIf="!loading()">
        <div class="toolbar-row">
          <span class="count">{{ selecionados().size }} selecionado(s) de {{ alunosFiltrados().length }} exibido(s)</span>
          <button mat-button type="button" (click)="marcarTodosFiltrados()">Marcar exibidos</button>
          <button mat-button type="button" (click)="desmarcarTodosFiltrados()">Desmarcar exibidos</button>
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
            <span class="outra-turma" *ngIf="estaEmOutraTurma(a)"
                  matTooltip="Ao marcar, o aluno sai da turma atual">
              <mat-icon>swap_horiz</mat-icon> {{ a.groupCode || a.groupName }}
            </span>
          </label>
        </div>

        <ng-template #vazio>
          <div class="center muted">
            <p *ngIf="alunos().length">Nenhum aluno corresponde à busca.</p>
            <p *ngIf="!alunos().length">
              Nenhum aluno ativo cadastrado. Importe a lista de alunos antes de montar a turma.
            </p>
          </div>
        </ng-template>
      </ng-container>
    </mat-dialog-content>

    <mat-dialog-actions align="end">
      <button mat-button (click)="dialogRef.close(null)">Cancelar</button>
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
    .search { width: 100%; }
    .center { display: flex; justify-content: center; padding: 24px; }
    .muted { color: #6B7280; font-size: 0.85rem; text-align: center; }
    .toolbar-row {
      display: flex; align-items: center; gap: 8px; flex-wrap: wrap;
      margin-bottom: 4px;
      .count { flex: 1; font-size: 0.78rem; color: #6B7280; }
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
    .nome { font-size: 0.86rem; font-weight: 500; }
    .meta { font-size: 0.72rem; color: #6B7280; }
    .outra-turma {
      display: inline-flex; align-items: center; gap: 3px;
      font-size: 0.7rem; color: #854d0e; background: #fef9c3;
      padding: 2px 8px; border-radius: 9999px; flex-shrink: 0;
      mat-icon { font-size: 14px; width: 14px; height: 14px; }
    }
    .btn-spinner { display: inline-block; margin-right: 8px; }
  `]
})
export class VincularAlunosDialogComponent implements OnInit {
  alunos = signal<UserDto[]>([]);
  selecionados = signal<Set<string>>(new Set());
  loading = signal(true);
  saving = signal(false);

  filtro = '';
  filtroSignal = signal('');

  /** Vínculos existentes no servidor, usados para calcular o que mudou. */
  private originais = new Set<string>();

  alunosFiltrados = computed(() => {
    const termo = this.filtroSignal().trim().toLowerCase();
    if (!termo) return this.alunos();
    return this.alunos().filter(a =>
      a.fullName.toLowerCase().includes(termo) || (a.rgm ?? '').toLowerCase().includes(termo)
    );
  });

  constructor(
    public dialogRef: MatDialogRef<VincularAlunosDialogComponent>,
    private usersService: UsersService,
    @Inject(MAT_DIALOG_DATA) public data: VincularAlunosDialogData
  ) {}

  ngOnInit(): void {
    this.usersService.getAll().subscribe({
      next: (todos) => {
        const ativos = todos.filter(u => u.role === 'aluno' && u.isActive !== false);
        this.alunos.set(ativos);
        this.originais = new Set(ativos.filter(a => a.groupId === this.data.group.id).map(a => a.id));
        this.selecionados.set(new Set(this.originais));
        this.loading.set(false);
      },
      error: () => this.loading.set(false)
    });
  }

  estaEmOutraTurma(a: UserDto): boolean {
    return !!a.groupId && a.groupId !== this.data.group.id;
  }

  alternar(id: string): void {
    this.selecionados.update(atual => {
      const proximo = new Set(atual);
      proximo.has(id) ? proximo.delete(id) : proximo.add(id);
      return proximo;
    });
  }

  marcarTodosFiltrados(): void {
    const ids = this.alunosFiltrados().map(a => a.id);
    this.selecionados.update(atual => new Set([...atual, ...ids]));
  }

  desmarcarTodosFiltrados(): void {
    const ids = new Set(this.alunosFiltrados().map(a => a.id));
    this.selecionados.update(atual => new Set([...atual].filter(id => !ids.has(id))));
  }

  temAlteracoes(): boolean {
    return this.adicionados().length > 0 || this.removidos().length > 0;
  }

  private adicionados(): string[] {
    return [...this.selecionados()].filter(id => !this.originais.has(id));
  }

  private removidos(): string[] {
    return [...this.originais].filter(id => !this.selecionados().has(id));
  }

  salvar(): void {
    const paraVincular = this.adicionados();
    const paraDesvincular = this.removidos();
    if (!paraVincular.length && !paraDesvincular.length) return;

    this.saving.set(true);
    const chamadas = [
      ...paraVincular.map(id => this.usersService.assignGroup(id, this.data.group.id)),
      ...paraDesvincular.map(id => this.usersService.assignGroup(id, null))
    ];

    forkJoin(chamadas.length ? chamadas : [of(void 0)]).subscribe({
      next: () => {
        this.saving.set(false);
        this.dialogRef.close({
          vinculados: paraVincular.length,
          desvinculados: paraDesvincular.length
        } as VincularAlunosResult);
      },
      error: () => this.saving.set(false)
    });
  }

  turnoLabel(shift: string): string {
    return { manha: 'Manhã', tarde: 'Tarde', noite: 'Noite' }[shift] ?? shift;
  }
}
