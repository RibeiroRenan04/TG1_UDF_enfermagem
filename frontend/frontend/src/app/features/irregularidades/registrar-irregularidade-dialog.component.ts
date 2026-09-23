import { Component, Inject, OnInit, computed, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatIconModule } from '@angular/material/icon';
import { MatStepperModule } from '@angular/material/stepper';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { IrregularitiesService } from '../../core/services/irregularities.service';
import { AttendanceService } from '../../core/services/attendance.service';
import { AttendanceRecord, IrregularityType } from '../../core/models/models';
import { mensagemErro } from '../../core/utils/api-error';
import { hojeIso } from '../../core/utils/data-br';

/** Opção "não é sobre um ponto": escolha explícita, para o passo 1 não avançar em branco. */
const SEM_PONTO = 'nenhum';

/** Ponto já escolhido pela tela de origem (histórico), quando houver. */
export interface RegistrarIrregularidadeData {
  registro?: AttendanceRecord;
}

/**
 * Registro de uma irregularidade de ponto pelo próprio aluno.
 *
 * O fluxo é guiado em três passos — qual ponto, o que houve, conferir e enviar —
 * porque o formulário único levava o aluno a escolher tipo e data antes mesmo de
 * lembrar de qual registro estava falando, e a ocorrência chegava ao preceptor
 * sem vínculo com o ponto.
 *
 * Quando a tela de origem já sabe o ponto (o botão de contestação do histórico),
 * ele vem pronto: o passo 1 vira uma confirmação, e tipo e data são deduzidos do
 * próprio registro.
 */
@Component({
  selector: 'app-registrar-irregularidade-dialog',
  standalone: true,
  imports: [
    CommonModule, ReactiveFormsModule,
    MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule,
    MatSelectModule, MatDatepickerModule,
    MatIconModule, MatStepperModule, MatProgressSpinnerModule
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
          Enquanto a análise não terminar, este ponto não aceita outra contestação.
        </div>
      </div>

      <!-- O cabeçalho do mat-stepper trunca os rótulos na largura do modal: este o substitui. -->
      <ol class="passos">
        <li *ngFor="let rotulo of passos; let i = index"
            [class.ativo]="i === passo()" [class.feito]="i < passo()"
            [attr.aria-current]="i === passo() ? 'step' : null">
          <span class="passo-num">
            <mat-icon *ngIf="i < passo(); else numero">check</mat-icon>
            <ng-template #numero>{{ i + 1 }}</ng-template>
          </span>
          <span class="passo-rotulo">{{ rotulo }}</span>
        </li>
      </ol>

      <mat-stepper #stepper linear [selectedIndex]="passo()" (selectionChange)="passo.set($event.selectedIndex)">

        <!-- ── 1. Qual ponto ─────────────────────────────────────────────── -->
        <mat-step [stepControl]="form.controls.attendanceRecordId" label="Qual ponto">
          <ng-container *ngIf="registroFixo() as r; else escolherPonto">
            <div class="ponto-fixo">
              <mat-icon>event_available</mat-icon>
              <div>
                <strong>{{ r.recordedAt | date:'dd/MM/yyyy HH:mm' }}</strong>
                <div class="sub">
                  {{ r.type === 'check_in' ? 'Entrada' : 'Saída' }}
                  <span *ngIf="r.locationName"> · {{ r.locationName }}</span>
                  · situação {{ r.status }}
                </div>
              </div>
            </div>
          </ng-container>

          <ng-template #escolherPonto>
            <p class="passo-ajuda">
              Escolha o registro de ponto que você quer contestar. Vincular a ocorrência ao
              ponto poupa o preceptor e o professor de procurá-lo.
            </p>

            <mat-form-field appearance="outline" class="full">
              <mat-label>Registro de ponto</mat-label>
              <mat-select [formControl]="form.controls.attendanceRecordId"
                          (selectionChange)="aoEscolherPonto()">
                <mat-option [value]="semPonto">Não é sobre um ponto registrado</mat-option>
                <mat-option *ngFor="let r of contestaveis()" [value]="r.id">
                  {{ r.recordedAt | date:'dd/MM/yy HH:mm' }} —
                  {{ r.type === 'check_in' ? 'Entrada' : 'Saída' }}
                  <span *ngIf="r.locationName">· {{ r.locationName }}</span>
                </mat-option>
              </mat-select>
              <mat-hint *ngIf="!contestaveis().length">
                Nenhum ponto disponível para contestação.
              </mat-hint>
              <mat-error>Escolha o ponto ou indique que não é sobre um ponto registrado</mat-error>
            </mat-form-field>

            <div class="bloqueados" *ngIf="bloqueados().length">
              <mat-icon>lock</mat-icon>
              <span>
                {{ bloqueados().length }} ponto(s) já têm uma contestação em análise e não
                aparecem na lista. Eles voltam a aceitar uma nova solicitação se o professor
                recusar a atual.
              </span>
            </div>
          </ng-template>

          <div class="passo-acoes">
            <button mat-button (click)="dialogRef.close(false)">Cancelar</button>
            <button mat-raised-button color="primary" matStepperNext
                    [disabled]="form.controls.attendanceRecordId.invalid">Continuar</button>
          </div>
        </mat-step>

        <!-- ── 2. O que aconteceu ────────────────────────────────────────── -->
        <mat-step [stepControl]="form" label="O que aconteceu">
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
              <mat-hint *ngIf="pontoSelecionado()">Preenchida a partir do ponto escolhido.</mat-hint>
              <mat-error *ngIf="form.controls.occurredOn.hasError('required')">Informe a data</mat-error>
            </mat-form-field>

            <mat-form-field appearance="outline">
              <mat-label>O que aconteceu?</mat-label>
              <textarea matInput rows="4" formControlName="description"
                        placeholder="Descreva a situação com o máximo de detalhes: horário, motivo e o que foi combinado."></textarea>
              <mat-hint align="end">{{ form.controls.description.value.length }}/2000</mat-hint>
              <mat-error *ngIf="form.controls.description.hasError('minlength')">
                Descreva com pelo menos 10 caracteres
              </mat-error>
            </mat-form-field>
          </form>

          <div class="passo-acoes">
            <button mat-button matStepperPrevious>Voltar</button>
            <button mat-raised-button color="primary" matStepperNext [disabled]="form.invalid">
              Revisar
            </button>
          </div>
        </mat-step>

        <!-- ── 3. Conferir e enviar ──────────────────────────────────────── -->
        <mat-step label="Conferir e enviar">
          <dl class="resumo">
            <dt>Ponto contestado</dt>
            <dd>
              <ng-container *ngIf="pontoSelecionado() as p; else semPonto">
                {{ p.recordedAt | date:'dd/MM/yyyy HH:mm' }} —
                {{ p.type === 'check_in' ? 'Entrada' : 'Saída' }}
                <span *ngIf="p.locationName">· {{ p.locationName }}</span>
              </ng-container>
              <ng-template #semPonto>Nenhum ponto vinculado</ng-template>
            </dd>
            <dt>Tipo</dt><dd>{{ rotuloTipo(form.controls.type.value) }}</dd>
            <dt>Data da ocorrência</dt><dd>{{ form.controls.occurredOn.value | date:'dd/MM/yyyy' }}</dd>
            <dt>Descrição</dt><dd class="resumo-descricao">{{ form.controls.description.value }}</dd>
          </dl>

          <p class="erro" *ngIf="erro()">{{ erro() }}</p>

          <div class="passo-acoes">
            <button mat-button matStepperPrevious [disabled]="busy()">Voltar</button>
            <button mat-raised-button color="primary" [disabled]="form.invalid || busy()" (click)="onSubmit()">
              <mat-spinner *ngIf="busy()" diameter="18" style="display:inline-block;margin-right:8px"></mat-spinner>
              <span *ngIf="!busy()">Enviar ao preceptor</span>
            </button>
          </div>
        </mat-step>
      </mat-stepper>
    </mat-dialog-content>
  `,
  styles: [`
    mat-dialog-content { padding-top: 8px; }
    .form { display: flex; flex-direction: column; gap: 14px; }
    .full { width: 100%; }
    .fluxo-box {
      display: flex; gap: 10px; align-items: flex-start;
      background: #eff6ff; border: 1px solid #bfdbfe; color: #1e40af;
      border-radius: 8px; padding: 10px 12px; margin-bottom: 20px;
      font-size: 0.78rem; line-height: 1.5;
      mat-icon { font-size: 18px; width: 18px; height: 18px; flex-shrink: 0; margin-top: 1px; }
    }

    /* Indicador de etapas */
    .passos {
      display: flex; align-items: center;
      list-style: none; margin: 0 0 20px; padding: 0;
      li {
        display: flex; align-items: center; gap: 8px; flex: 1;
        font-size: 0.8rem; color: #6B7280; white-space: nowrap;
      }
      li:last-child { flex: none; }
      li:not(:last-child)::after {
        content: ''; flex: 1; height: 2px; min-width: 16px;
        margin: 0 4px; border-radius: 1px; background: #e5e7eb;
      }
      li.feito::after { background: var(--primary); }
      .passo-num {
        display: inline-flex; align-items: center; justify-content: center; flex-shrink: 0;
        width: 24px; height: 24px; border-radius: 50%;
        border: 2px solid #d1d5db; box-sizing: border-box;
        font-size: 0.75rem; font-weight: 600; line-height: 1;
        mat-icon { font-size: 16px; width: 16px; height: 16px; }
      }
      li.ativo { color: var(--foreground); font-weight: 600; }
      li.ativo .passo-num { background: var(--primary); border-color: var(--primary); color: #fff; }
      li.feito .passo-num { background: #e0f5fd; border-color: var(--primary); color: var(--primary); }
    }

    /* O stepper só troca o conteúdo: sem cabeçalho próprio, sem recuo lateral */
    mat-stepper { --mat-stepper-container-color: transparent; }
    :host ::ng-deep .mat-horizontal-stepper-header-container { display: none; }
    :host ::ng-deep .mat-horizontal-content-container { padding: 0; }

    .passo-ajuda { font-size: 0.8rem; color: #6B7280; margin: 0 0 12px; line-height: 1.5; }
    .ponto-fixo {
      display: flex; gap: 10px; align-items: center;
      background: #f9fafb; border: 1px solid #e5e7eb; border-radius: 8px;
      padding: 12px; margin-bottom: 8px; font-size: 0.85rem;
      mat-icon { color: #4b5563; }
      .sub { color: #6B7280; font-size: 0.75rem; margin-top: 2px; }
    }
    .bloqueados {
      display: flex; gap: 8px; align-items: flex-start;
      background: #fffbeb; border: 1px solid #fde68a; color: #92400e;
      border-radius: 8px; padding: 10px 12px; margin-top: 4px;
      font-size: 0.75rem; line-height: 1.5;
      mat-icon { font-size: 18px; width: 18px; height: 18px; flex-shrink: 0; }
    }
    .resumo {
      display: grid; grid-template-columns: auto 1fr; gap: 6px 16px;
      font-size: 0.85rem; margin: 4px 0 8px;
      dt { color: #6B7280; }
      dd { margin: 0; }
      .resumo-descricao { white-space: pre-wrap; }
    }
    .passo-acoes { display: flex; justify-content: flex-end; gap: 8px; margin-top: 20px; }
    .erro { color: #b91c1c; font-size: 0.85rem; margin: 8px 0 0; }
    :host { display: block; }

    /* No celular só a etapa atual mostra o nome */
    @media (max-width: 480px) {
      .passos li:not(.ativo) .passo-rotulo { display: none; }
    }
  `]
})
export class RegistrarIrregularidadeDialogComponent implements OnInit {
  busy = signal(false);
  erro = signal('');
  passo = signal(0);
  readonly passos = ['Ponto', 'Ocorrência', 'Conferir e enviar'];
  readonly semPonto = SEM_PONTO;
  registros = signal<AttendanceRecord[]>([]);
  readonly hoje = hojeIso();

  /** Ponto informado pela tela de origem — o passo 1 vira só confirmação. */
  readonly registroFixo = signal<AttendanceRecord | null>(null);

  readonly tipos: { valor: IrregularityType; rotulo: string }[] = [
    { valor: 'atraso',                rotulo: 'Cheguei atrasado(a)' },
    { valor: 'esquecimento_checkin',  rotulo: 'Esqueci de registrar a entrada' },
    { valor: 'esquecimento_checkout', rotulo: 'Esqueci de registrar a saída' },
    { valor: 'fora_do_local',         rotulo: 'Registro fora do local' },
    { valor: 'falta_justificada',     rotulo: 'Falta justificada' },
    { valor: 'problema_tecnico',      rotulo: 'Problema técnico (GPS, câmera, app)' },
    { valor: 'outro',                 rotulo: 'Outro motivo' }
  ];

  form = this.fb.nonNullable.group({
    type:               ['atraso' as IrregularityType, Validators.required],
    occurredOn:         [hojeIso(), Validators.required],
    attendanceRecordId: this.fb.control<string | null>(null, Validators.required),
    description:        ['', [Validators.required, Validators.minLength(10), Validators.maxLength(2000)]]
  });

  /** Pontos que ainda aceitam contestação — os demais estão em análise. */
  contestaveis = computed(() => this.registros().filter(r => !r.hasOpenIrregularity));

  /** Pontos escondidos da lista porque já têm uma ocorrência em andamento. */
  bloqueados = computed(() => this.registros().filter(r => r.hasOpenIrregularity));

  /** Ponto que a ocorrência vai referenciar (o fixo ou o escolhido na lista). */
  pontoSelecionado = signal<AttendanceRecord | null>(null);

  constructor(
    private fb: FormBuilder,
    public dialogRef: MatDialogRef<RegistrarIrregularidadeDialogComponent>,
    private service: IrregularitiesService,
    private attendance: AttendanceService,
    @Inject(MAT_DIALOG_DATA) private data: RegistrarIrregularidadeData | null
  ) {}

  ngOnInit(): void {
    const fixo = this.data?.registro;
    if (fixo) {
      this.registroFixo.set(fixo);
      this.pontoSelecionado.set(fixo);
      this.form.controls.attendanceRecordId.setValue(fixo.id);
      this.aplicarContexto(fixo);
      return;
    }

    // Sem ponto informado, o aluno escolhe entre os registros recentes.
    this.attendance.getAll(undefined, 30).subscribe({
      next: (r) => this.registros.set(r),
      error: () => {}
    });
  }

  /** Escolher o ponto já preenche data e tipo prováveis da ocorrência. */
  aoEscolherPonto(): void {
    const id = this.form.controls.attendanceRecordId.value;
    const ponto = this.registros().find(r => r.id === id) ?? null;
    this.pontoSelecionado.set(ponto);
    if (ponto) this.aplicarContexto(ponto);
  }

  /** Deduz data e tipo a partir do ponto, deixando o passo 2 quase pronto. */
  private aplicarContexto(r: AttendanceRecord): void {
    this.form.controls.occurredOn.setValue(r.recordedAt.slice(0, 10));
    this.form.controls.type.setValue(
      r.status === 'irregular' ? 'fora_do_local'
        : r.type === 'check_in' ? 'atraso' : 'esquecimento_checkout');
  }

  rotuloTipo(valor: IrregularityType): string {
    return this.tipos.find(t => t.valor === valor)?.rotulo ?? valor;
  }

  onSubmit(): void {
    if (this.form.invalid) return;
    this.busy.set(true);
    this.erro.set('');

    const v = this.form.getRawValue();
    this.service.create({
      type: v.type,
      occurredOn: v.occurredOn,
      description: v.description,
      attendanceRecordId: v.attendanceRecordId === SEM_PONTO ? undefined : v.attendanceRecordId ?? undefined
    }).subscribe({
      next: () => { this.busy.set(false); this.dialogRef.close(true); },
      error: (err) => {
        this.busy.set(false);
        this.erro.set(mensagemErro(err, 'Erro ao registrar a irregularidade.'));
      }
    });
  }
}
