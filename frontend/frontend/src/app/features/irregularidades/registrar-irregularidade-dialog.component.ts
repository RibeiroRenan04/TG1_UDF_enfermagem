import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatNativeDateModule } from '@angular/material/core';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { IrregularitiesService } from '../../core/services/irregularities.service';
import { AttendanceService } from '../../core/services/attendance.service';
import { AttendanceRecord, IrregularityType } from '../../core/models/models';

/** Registro de uma irregularidade de ponto pelo próprio aluno. */
@Component({
  selector: 'app-registrar-irregularidade-dialog',
  standalone: true,
  imports: [
    CommonModule, ReactiveFormsModule,
    MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule,
    MatSelectModule, MatDatepickerModule, MatNativeDateModule,
    MatIconModule, MatProgressSpinnerModule
  ],
  template: `
    <h2 mat-dialog-title>Registrar irregularidade no ponto</h2>

    <mat-dialog-content>
      <div class="fluxo-box">
        <mat-icon>route</mat-icon>
        <div>
          Sua ocorrência vai para o <strong>preceptor</strong>, que registra a ciência e
          pode acrescentar uma observação. Em seguida ela é
          <strong>encaminhada ao professor responsável</strong>, que aprova ou nega.
        </div>
      </div>

      <form [formGroup]="form" class="form">
        <mat-form-field appearance="outline">
          <mat-label>Tipo de ocorrência</mat-label>
          <mat-select formControlName="type">
            <mat-option *ngFor="let t of tipos" [value]="t.valor">{{ t.rotulo }}</mat-option>
          </mat-select>
        </mat-form-field>

        <mat-form-field appearance="outline">
          <mat-label>Data da ocorrência</mat-label>
          <input matInput [matDatepicker]="picker" formControlName="occurredOn" [max]="hoje">
          <mat-datepicker-toggle matIconSuffix [for]="picker"></mat-datepicker-toggle>
          <mat-datepicker #picker></mat-datepicker>
          <mat-error *ngIf="form.get('occurredOn')?.hasError('required')">Informe a data</mat-error>
        </mat-form-field>

        <mat-form-field appearance="outline" *ngIf="registros().length">
          <mat-label>Registro de ponto relacionado (opcional)</mat-label>
          <mat-select formControlName="attendanceRecordId">
            <mat-option [value]="null">Nenhum</mat-option>
            <mat-option *ngFor="let r of registros()" [value]="r.id">
              {{ r.recordedAt | date:'dd/MM/yy HH:mm' }} —
              {{ r.type === 'check_in' ? 'Entrada' : 'Saída' }}
              ({{ r.status }})
            </mat-option>
          </mat-select>
        </mat-form-field>

        <mat-form-field appearance="outline">
          <mat-label>O que aconteceu?</mat-label>
          <textarea matInput rows="4" formControlName="description"
                    placeholder="Descreva a situação com o máximo de detalhes: horário, motivo e o que foi combinado."></textarea>
          <mat-hint align="end">{{ form.get('description')?.value?.length || 0 }}/2000</mat-hint>
          <mat-error *ngIf="form.get('description')?.hasError('minlength')">
            Descreva com pelo menos 10 caracteres
          </mat-error>
        </mat-form-field>
      </form>

      <p class="erro" *ngIf="erro()">{{ erro() }}</p>
    </mat-dialog-content>

    <mat-dialog-actions align="end">
      <button mat-button (click)="dialogRef.close(false)">Cancelar</button>
      <button mat-raised-button color="primary" [disabled]="form.invalid || busy()" (click)="onSubmit()">
        <mat-spinner *ngIf="busy()" diameter="18" style="display:inline-block;margin-right:8px"></mat-spinner>
        <span *ngIf="!busy()">Enviar</span>
      </button>
    </mat-dialog-actions>
  `,
  styles: [`
    .form { display: flex; flex-direction: column; gap: 14px; min-width: 400px; }
    .fluxo-box {
      display: flex; gap: 8px; align-items: flex-start;
      background: #eff6ff; border: 1px solid #bfdbfe; color: #1e40af;
      border-radius: 8px; padding: 10px 12px; margin-bottom: 16px;
      font-size: 0.78rem; line-height: 1.5;
      mat-icon { font-size: 20px; width: 20px; height: 20px; flex-shrink: 0; }
    }
    .erro { color: #b91c1c; font-size: 0.85rem; margin: 8px 0 0; }
  `]
})
export class RegistrarIrregularidadeDialogComponent implements OnInit {
  busy = signal(false);
  erro = signal('');
  registros = signal<AttendanceRecord[]>([]);
  readonly hoje = new Date();

  readonly tipos: { valor: IrregularityType; rotulo: string }[] = [
    { valor: 'atraso',                rotulo: 'Cheguei atrasado(a)' },
    { valor: 'esquecimento_checkin',  rotulo: 'Esqueci de registrar a entrada' },
    { valor: 'esquecimento_checkout', rotulo: 'Esqueci de registrar a saída' },
    { valor: 'fora_do_local',         rotulo: 'Registro fora do local' },
    { valor: 'falta_justificada',     rotulo: 'Falta justificada' },
    { valor: 'problema_tecnico',      rotulo: 'Problema técnico (GPS, câmera, app)' },
    { valor: 'outro',                 rotulo: 'Outro motivo' }
  ];

  form = this.fb.group({
    type:               ['atraso' as IrregularityType, Validators.required],
    occurredOn:         [new Date(), Validators.required],
    attendanceRecordId: [null as string | null],
    description:        ['', [Validators.required, Validators.minLength(10), Validators.maxLength(2000)]]
  });

  constructor(
    private fb: FormBuilder,
    public dialogRef: MatDialogRef<RegistrarIrregularidadeDialogComponent>,
    private service: IrregularitiesService,
    private attendance: AttendanceService
  ) {}

  ngOnInit(): void {
    // Vincular a ocorrência ao registro de ponto poupa o professor de procurá-lo.
    this.attendance.getAll(undefined, 30).subscribe({
      next: (r) => this.registros.set(r),
      error: () => {}
    });
  }

  onSubmit(): void {
    if (this.form.invalid) return;
    this.busy.set(true);
    this.erro.set('');

    const v = this.form.value;
    this.service.create({
      type: v.type as IrregularityType,
      occurredOn: this.formatarData(v.occurredOn as Date),
      description: v.description!,
      attendanceRecordId: v.attendanceRecordId ?? undefined
    }).subscribe({
      next: () => { this.busy.set(false); this.dialogRef.close(true); },
      error: (err) => {
        this.busy.set(false);
        this.erro.set(err?.error?.message ?? 'Erro ao registrar a irregularidade.');
      }
    });
  }

  /** A API espera DateOnly ("yyyy-MM-dd"); usa a data local para não voltar um dia. */
  private formatarData(d: Date): string {
    const mes = `${d.getMonth() + 1}`.padStart(2, '0');
    const dia = `${d.getDate()}`.padStart(2, '0');
    return `${d.getFullYear()}-${mes}-${dia}`;
  }
}
