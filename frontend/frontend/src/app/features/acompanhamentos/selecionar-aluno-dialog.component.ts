import { Component, OnInit, computed, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatExpansionModule } from '@angular/material/expansion';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { FollowupsService } from '../../core/services/followups.service';
import { ScheduleStudents, StudentLookup } from '../../core/models/models';

/**
 * Lista dos alunos alocados nos rodízios do preceptor. Escolher um aluno aqui
 * devolve o cadastro completo já com o contexto do rodízio, dispensando a busca
 * manual pelo RGM.
 */
@Component({
  selector: 'app-selecionar-aluno-dialog',
  standalone: true,
  imports: [
    CommonModule, FormsModule,
    MatDialogModule, MatButtonModule, MatIconModule,
    MatFormFieldModule, MatInputModule, MatExpansionModule, MatProgressSpinnerModule
  ],
  template: `
    <h2 mat-dialog-title>Alunos alocados nos seus rodízios</h2>

    <mat-dialog-content>
      <mat-form-field appearance="outline" class="busca" subscriptSizing="dynamic">
        <mat-label>Buscar por nome ou RGM</mat-label>
        <input matInput [(ngModel)]="termo" (ngModelChange)="termoSignal.set($event)" autocomplete="off">
        <mat-icon matPrefix>search</mat-icon>
      </mat-form-field>

      <div *ngIf="loading()" class="carregando"><mat-spinner diameter="36"></mat-spinner></div>

      <ng-container *ngIf="!loading()">
        <div *ngIf="!rodiziosFiltrados().length" class="vazio">
          <mat-icon>groups_off</mat-icon>
          <p *ngIf="!rodizios().length">
            Você ainda não tem rodízios alocados. Fale com o professor responsável.
          </p>
          <p *ngIf="rodizios().length">Nenhum aluno encontrado para “{{ termo }}”.</p>
        </div>

        <mat-accordion multi>
          <mat-expansion-panel *ngFor="let r of rodiziosFiltrados(); let i = index"
                               [expanded]="r.current || rodiziosFiltrados().length === 1 || !!termo">
            <mat-expansion-panel-header>
              <mat-panel-title>
                <span class="rodizio-titulo">
                  {{ r.locationName || 'Local não definido' }}
                  <span class="chip-atual" *ngIf="r.current">Em andamento</span>
                </span>
              </mat-panel-title>
              <mat-panel-description>
                {{ r.groupCode }} · {{ turnoLabel(r.shift) }} ·
                {{ r.startDate | date:'dd/MM/yy' }}–{{ r.endDate | date:'dd/MM/yy' }}
                <span class="contagem">{{ r.students.length }} aluno(s)</span>
              </mat-panel-description>
            </mat-expansion-panel-header>

            <div class="aluno-lista">
              <button type="button" class="aluno-item" *ngFor="let a of r.students"
                      (click)="escolher(a)">
                <span class="aluno-avatar">{{ a.fullName.charAt(0).toUpperCase() }}</span>
                <span class="aluno-dados">
                  <span class="aluno-nome">{{ a.fullName }}</span>
                  <span class="aluno-meta">
                    RGM {{ a.rgm || '—' }}
                    <ng-container *ngIf="a.semester"> · {{ a.semester }}° semestre</ng-container>
                  </span>
                </span>
                <mat-icon>chevron_right</mat-icon>
              </button>
              <p class="sem-aluno" *ngIf="!r.students.length">
                Nenhum aluno vinculado a esta turma.
              </p>
            </div>
          </mat-expansion-panel>
        </mat-accordion>
      </ng-container>
    </mat-dialog-content>

    <mat-dialog-actions align="end">
      <button mat-button (click)="dialogRef.close(null)">Fechar</button>
    </mat-dialog-actions>
  `,
  styles: [`
    .busca { width: 100%; min-width: 420px; margin-bottom: 14px; }
    .carregando { display: flex; justify-content: center; padding: 32px; }
    .vazio {
      text-align: center; color: #6B7280; padding: 28px 8px;
      mat-icon { font-size: 40px; width: 40px; height: 40px; color: #d1d5db; }
      p { font-size: 0.85rem; margin: 8px 0 0; }
    }
    .rodizio-titulo { display: flex; align-items: center; gap: 8px; font-weight: 600; }
    .chip-atual {
      font-size: 0.65rem; font-weight: 700; text-transform: uppercase;
      letter-spacing: 0.04em; padding: 2px 8px; border-radius: 9999px;
      background: #dcfce7; color: #166534;
    }
    .contagem { margin-left: 8px; color: #9ca3af; }
    .aluno-lista { display: flex; flex-direction: column; }
    .aluno-item {
      display: flex; align-items: center; gap: 10px;
      padding: 8px 6px; border: none; background: none; cursor: pointer;
      font: inherit; text-align: left; border-radius: 8px;
      border-bottom: 1px solid #f3f4f6;
      &:last-of-type { border-bottom: none; }
      &:hover { background: rgb(0 86 166 / 0.06); }
      mat-icon { color: #9ca3af; }
    }
    .aluno-avatar {
      width: 32px; height: 32px; border-radius: 50%; flex-shrink: 0;
      background: #0056A6; color: #fff;
      display: flex; align-items: center; justify-content: center;
      font-size: 0.85rem; font-weight: 600;
    }
    .aluno-dados { flex: 1; display: flex; flex-direction: column; min-width: 0; }
    .aluno-nome { font-size: 0.9rem; color: #111827; }
    .aluno-meta { font-size: 0.75rem; color: #6B7280; }
    .sem-aluno { font-size: 0.8rem; color: #9ca3af; margin: 8px 0; }
  `]
})
export class SelecionarAlunoDialogComponent implements OnInit {
  rodizios = signal<ScheduleStudents[]>([]);
  loading = signal(true);
  termo = '';
  termoSignal = signal('');

  /** Filtra por nome ou RGM, mantendo só os rodízios que ainda têm alunos. */
  rodiziosFiltrados = computed(() => {
    const busca = this.termoSignal().trim().toLowerCase();
    if (!busca) return this.rodizios();

    return this.rodizios()
      .map(r => ({
        ...r,
        students: r.students.filter(a =>
          a.fullName.toLowerCase().includes(busca) || (a.rgm ?? '').includes(busca))
      }))
      .filter(r => r.students.length > 0);
  });

  constructor(
    public dialogRef: MatDialogRef<SelecionarAlunoDialogComponent>,
    private followupsService: FollowupsService
  ) {}

  ngOnInit(): void {
    this.followupsService.getMySchedules().subscribe({
      next: (r) => { this.rodizios.set(r); this.loading.set(false); },
      error: () => this.loading.set(false)
    });
  }

  turnoLabel(shift: string): string {
    return ({ manha: 'Manhã', tarde: 'Tarde', noite: 'Noite' } as Record<string, string>)[shift] ?? shift;
  }

  escolher(aluno: StudentLookup): void {
    this.dialogRef.close(aluno);
  }
}
