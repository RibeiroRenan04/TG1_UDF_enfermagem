import { Component, OnInit, computed, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatDividerModule } from '@angular/material/divider';
import { FollowupsService } from '../../core/services/followups.service';
import { AuthService } from '../../core/services/auth.service';
import { FormativeFollowup } from '../../core/models/models';

/** Escala de frequência usada nas dimensões comportamentais. */
const ESCALA = ['nao_observado', 'raramente', 'as_vezes', 'frequentemente', 'sempre'];

interface Dimensao { campo: string; rotulo: string; }
interface Bloco { titulo: string; itens: Dimensao[]; }

@Component({
  selector: 'app-acompanhamentos',
  standalone: true,
  imports: [
    CommonModule, ReactiveFormsModule,
    MatCardModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule,
    MatSelectModule, MatProgressSpinnerModule, MatSnackBarModule,
    MatTooltipModule, MatDividerModule
  ],
  templateUrl: './acompanhamentos.component.html',
  styleUrls: ['./acompanhamentos.component.scss']
})
export class AcompanhamentosComponent implements OnInit {
  followups = signal<FormativeFollowup[]>([]);
  loading = signal(true);
  showForm = signal(false);
  saving = signal(false);
  selectedFollowup = signal<FormativeFollowup | null>(null);
  role = this.auth.role;

  studentName = signal<string>('');
  lookupError = signal<string>('');
  lookingUp = signal(false);
  /** Campos preenchidos automaticamente a partir do cadastro do aluno. */
  autoPreenchidos = signal<string[]>([]);

  /** O acompanhamento do aluno é realizado pelo preceptor. */
  podeRegistrar = computed(() => this.role() === 'preceptor');
  /** O aluno dá ciência do acompanhamento realizado. */
  podeDarCiencia = computed(() => this.role() === 'aluno');
  /** O supervisor apenas consulta os relatórios finalizados. */
  somenteLeitura = computed(() => this.role() === 'supervisor');

  readonly escala = ESCALA;
  readonly rotulosEscala: Record<string, string> = {
    nao_observado: 'Não observado',
    raramente: 'Raramente',
    as_vezes: 'Às vezes',
    frequentemente: 'Frequentemente',
    sempre: 'Sempre'
  };

  readonly rotulosStatus: Record<string, string> = {
    rascunho: 'Rascunho',
    finalizado_preceptor: 'Aguardando ciência do aluno',
    finalizado_aluno: 'Concluído com ciência do aluno'
  };

  readonly blocos: Bloco[] = [
    {
      titulo: 'Postura profissional e ética',
      itens: [
        { campo: 'posturaPontualidade', rotulo: 'Pontualidade e assiduidade' },
        { campo: 'posturaEtica', rotulo: 'Conduta ética e sigilo' },
        { campo: 'posturaResponsabilidade', rotulo: 'Responsabilidade' }
      ]
    },
    {
      titulo: 'Comunicação e trabalho em equipe',
      itens: [
        { campo: 'comunicacaoEquipe', rotulo: 'Comunicação com a equipe' },
        { campo: 'comunicacaoPaciente', rotulo: 'Comunicação com o paciente' },
        { campo: 'comunicacaoEscuta', rotulo: 'Escuta ativa' }
      ]
    },
    {
      titulo: 'Organização e segurança no cuidado',
      itens: [
        { campo: 'organizacaoPlanejamento', rotulo: 'Planejamento das atividades' },
        { campo: 'organizacaoSeguranca', rotulo: 'Segurança do paciente' },
        { campo: 'organizacaoRegistros', rotulo: 'Registros e documentação' }
      ]
    },
    {
      titulo: 'Participação e desenvolvimento',
      itens: [
        { campo: 'participacaoIniciativa', rotulo: 'Iniciativa' },
        { campo: 'participacaoAprendizado', rotulo: 'Busca por aprendizado' },
        { campo: 'participacaoAutocritica', rotulo: 'Autocrítica' }
      ]
    }
  ];

  form = this.fb.group({
    rgm: ['', Validators.required],
    studentId: ['', Validators.required], // preenchido pela busca por RGM
    scheduleId: [''],
    groupId: [''],
    locationId: [''],
    shift: [''],
    semester: [''],
    periodLabel: [''],
    followUpStart: [''],
    followUpEnd: [''],
    posturaPontualidade: [''],
    posturaEtica: [''],
    posturaResponsabilidade: [''],
    comunicacaoEquipe: [''],
    comunicacaoPaciente: [''],
    comunicacaoEscuta: [''],
    organizacaoPlanejamento: [''],
    organizacaoSeguranca: [''],
    organizacaoRegistros: [''],
    participacaoIniciativa: [''],
    participacaoAprendizado: [''],
    participacaoAutocritica: [''],
    potencialidades: [''],
    aspectosAprimorar: [''],
    situacoesRelevantes: [''],
    observacoesDocente: [''],
    evolucaoSemanal: ['']
  });

  constructor(
    private followupsService: FollowupsService,
    private auth: AuthService,
    private snackBar: MatSnackBar,
    private fb: FormBuilder
  ) {}

  ngOnInit(): void { this.load(); }

  load(): void {
    this.loading.set(true);
    this.followupsService.getAll().subscribe({
      next: (f) => { this.followups.set(f); this.loading.set(false); },
      error: () => { this.loading.set(false); this.snackBar.open('Erro ao carregar acompanhamentos', '', { duration: 3000 }); }
    });
  }

  /**
   * Busca o aluno pelo RGM e preenche automaticamente nome, período, turno,
   * semestre e a escala vigente, reduzindo o preenchimento manual e evitando
   * divergência com os dados já cadastrados.
   */
  lookupRgm(): void {
    const rgm = (this.form.get('rgm')?.value ?? '').trim();
    this.studentName.set('');
    this.lookupError.set('');
    this.autoPreenchidos.set([]);
    this.form.patchValue({ studentId: '', scheduleId: '', groupId: '', locationId: '' });
    if (!rgm) return;

    this.lookingUp.set(true);
    this.followupsService.getStudentByRgm(rgm).subscribe({
      next: (s) => {
        this.lookingUp.set(false);
        this.studentName.set(s.fullName);

        const preenchidos: string[] = ['Nome'];
        if (s.periodLabel) preenchidos.push('Período');
        if (s.semester) preenchidos.push('Semestre');
        if (s.shift) preenchidos.push('Turno');
        if (s.locationName) preenchidos.push('Local');
        this.autoPreenchidos.set(preenchidos);

        this.form.patchValue({
          studentId: s.studentId,
          scheduleId: s.scheduleId ?? '',
          groupId: s.groupId ?? '',
          locationId: s.locationId ?? '',
          shift: s.shift ?? '',
          semester: s.semester != null ? String(s.semester) : '',
          periodLabel: s.periodLabel ?? '',
          followUpStart: s.followUpStart ?? '',
          followUpEnd: s.followUpEnd ?? ''
        });
      },
      error: (err) => {
        this.lookingUp.set(false);
        this.lookupError.set(err?.error?.message ?? 'Aluno não encontrado para esse RGM.');
      }
    });
  }

  save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      this.snackBar.open('Informe um RGM válido de aluno.', '', { duration: 3000 });
      return;
    }

    this.saving.set(true);
    const dto = this.montarDto();
    const atual = this.selectedFollowup();
    const op = atual
      ? this.followupsService.update(atual.id, dto)
      : this.followupsService.create(dto);

    op.subscribe({
      next: () => {
        this.saving.set(false);
        this.snackBar.open('Acompanhamento salvo como rascunho.', '', { duration: 2500, panelClass: 'snack-success' });
        this.showForm.set(false);
        this.load();
      },
      error: (err) => {
        this.saving.set(false);
        this.snackBar.open(err?.error?.message ?? 'Erro ao salvar', '', { duration: 4000, panelClass: 'snack-error' });
      }
    });
  }

  /** Monta o payload conforme o contrato da API, enviando vazios como nulos. */
  private montarDto(): Partial<FormativeFollowup> {
    const valores = this.form.value as Record<string, string | null>;
    const dto: Record<string, unknown> = {};

    for (const [chave, valor] of Object.entries(valores)) {
      if (chave === 'rgm') continue;
      dto[chave] = valor === '' ? null : valor;
    }
    return dto as Partial<FormativeFollowup>;
  }

  editar(f: FormativeFollowup): void {
    this.selectedFollowup.set(f);
    this.studentName.set(f.studentName);
    this.lookupError.set('');
    this.autoPreenchidos.set([]);
    this.form.patchValue({
      rgm: f.studentRgm ?? '',
      studentId: f.studentId,
      scheduleId: f.scheduleId ?? '',
      groupId: f.groupId ?? '',
      locationId: f.locationId ?? '',
      shift: f.shift ?? '',
      semester: f.semester ?? '',
      periodLabel: f.periodLabel ?? '',
      followUpStart: f.followUpStart ?? '',
      followUpEnd: f.followUpEnd ?? '',
      posturaPontualidade: f.posturaPontualidade ?? '',
      posturaEtica: f.posturaEtica ?? '',
      posturaResponsabilidade: f.posturaResponsabilidade ?? '',
      comunicacaoEquipe: f.comunicacaoEquipe ?? '',
      comunicacaoPaciente: f.comunicacaoPaciente ?? '',
      comunicacaoEscuta: f.comunicacaoEscuta ?? '',
      organizacaoPlanejamento: f.organizacaoPlanejamento ?? '',
      organizacaoSeguranca: f.organizacaoSeguranca ?? '',
      organizacaoRegistros: f.organizacaoRegistros ?? '',
      participacaoIniciativa: f.participacaoIniciativa ?? '',
      participacaoAprendizado: f.participacaoAprendizado ?? '',
      participacaoAutocritica: f.participacaoAutocritica ?? '',
      potencialidades: f.potencialidades ?? '',
      aspectosAprimorar: f.aspectosAprimorar ?? '',
      situacoesRelevantes: f.situacoesRelevantes ?? '',
      observacoesDocente: f.observacoesDocente ?? '',
      evolucaoSemanal: f.evolucaoSemanal ?? ''
    });
    this.showForm.set(true);
  }

  /** Preceptor finaliza o acompanhamento e o libera para ciência do aluno. */
  finalizePreceptor(f: FormativeFollowup): void {
    const nome = this.auth.user()?.fullName ?? '';
    if (!confirm(`Finalizar o acompanhamento de ${f.studentName}? Depois de finalizado o conteúdo fica bloqueado e o aluno poderá dar ciência.`)) return;

    this.followupsService.finalizePreceptor(f.id, nome).subscribe({
      next: () => { this.snackBar.open('Acompanhamento finalizado. Aguardando ciência do aluno.', '', { duration: 3000, panelClass: 'snack-success' }); this.load(); },
      error: (err) => this.snackBar.open(err?.error?.message ?? 'Erro ao finalizar', '', { duration: 4000, panelClass: 'snack-error' })
    });
  }

  /** Aluno registra a ciência do acompanhamento realizado. */
  darCiencia(f: FormativeFollowup): void {
    const nome = this.auth.user()?.fullName ?? '';
    if (!confirm('Confirma que você tomou ciência deste acompanhamento formativo?')) return;

    this.followupsService.finalizeStudent(f.id, nome).subscribe({
      next: () => { this.snackBar.open('Ciência registrada!', '', { duration: 2500, panelClass: 'snack-success' }); this.load(); },
      error: (err) => this.snackBar.open(err?.error?.message ?? 'Erro ao registrar ciência', '', { duration: 4000, panelClass: 'snack-error' })
    });
  }

  openCreate(): void {
    this.selectedFollowup.set(null);
    this.studentName.set('');
    this.lookupError.set('');
    this.autoPreenchidos.set([]);
    this.form.reset({
      rgm: '', studentId: '', scheduleId: '', groupId: '', locationId: '',
      shift: '', semester: '', periodLabel: '', followUpStart: '', followUpEnd: '',
      posturaPontualidade: '', posturaEtica: '', posturaResponsabilidade: '',
      comunicacaoEquipe: '', comunicacaoPaciente: '', comunicacaoEscuta: '',
      organizacaoPlanejamento: '', organizacaoSeguranca: '', organizacaoRegistros: '',
      participacaoIniciativa: '', participacaoAprendizado: '', participacaoAutocritica: '',
      potencialidades: '', aspectosAprimorar: '', situacoesRelevantes: '',
      observacoesDocente: '', evolucaoSemanal: ''
    });
    this.showForm.set(true);
  }

  statusLabel(status: string): string { return this.rotulosStatus[status] ?? status; }

  escalaLabel(valor: string): string { return this.rotulosEscala[valor] ?? valor; }

  valorDimensao(f: FormativeFollowup, campo: string): string {
    const v = (f as unknown as Record<string, string | undefined>)[campo];
    return v ? (this.rotulosEscala[v] ?? v) : '—';
  }
}
