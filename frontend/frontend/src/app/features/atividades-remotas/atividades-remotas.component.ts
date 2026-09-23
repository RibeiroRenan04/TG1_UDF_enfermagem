import { Component, OnInit, computed, signal } from '@angular/core';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatExpansionModule } from '@angular/material/expansion';
import { MatTableModule } from '@angular/material/table';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatDividerModule } from '@angular/material/divider';
import { GroupsService } from '../../core/services/groups.service';
import { AuthService } from '../../core/services/auth.service';
import {
  AtividadesRemotasService, AtividadeRemotaForm
} from '../../core/services/atividades-remotas.service';
import {
  AtividadeRemota, AtividadeRemotaAluno, ParticipacaoAtividade,
  RotationSchedule, StudentGroup, TipoTarefaRemota
} from '../../core/models/models';
import { aplicarErrosServidor, mensagemErro } from '../../core/utils/api-error';
import { HoraPipe } from '../../core/utils/hora.pipe';
import { HoraInputDirective } from '../../core/utils/hora-input.directive';
import { hojeIso } from '../../core/utils/data-br';
import { MatPaginatorModule } from '@angular/material/paginator';
import { Paginacao, normalizarBusca } from '../../core/utils/paginacao';

/**
 * Atividades remotas.
 *
 * O professor cria a atividade, o sistema gera o código de presença e a tela
 * mostra quantos alunos do grupo já registraram participação. O aluno vê a mesma
 * lista sem o código — é ele que precisa vir do professor para comprovar o
 * acesso; o registro em si acontece na tela de presença.
 */
@Component({
  selector: 'app-atividades-remotas',
  standalone: true,
  imports: [
    MatDatepickerModule, CommonModule, ReactiveFormsModule,
    MatCardModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule,
    MatSelectModule, MatCheckboxModule, MatExpansionModule, MatTableModule,
    MatProgressSpinnerModule, MatSnackBarModule, MatTooltipModule, MatDividerModule, HoraPipe, HoraInputDirective, MatPaginatorModule
  ],
  templateUrl: './atividades-remotas.component.html',
  styleUrls: ['./atividades-remotas.component.scss']
})
export class AtividadesRemotasComponent implements OnInit {
  atividades = signal<AtividadeRemota[]>([]);
  readonly busca = signal('');
  readonly filtradas = computed(() => {
    const termo = normalizarBusca(this.busca());
    return termo
      ? this.atividades().filter(a =>
          [a.title, a.groupCode, a.presenceCode].some(c => normalizarBusca(c).includes(termo)))
      : this.atividades();
  });
  readonly paginacao = new Paginacao(this.filtradas);
  minhas = signal<AtividadeRemotaAluno[]>([]);
  groups = signal<StudentGroup[]>([]);
  schedules = signal<RotationSchedule[]>([]);
  participacoes = signal<Record<string, ParticipacaoAtividade[]>>({});
  loading = signal(true);
  saving = signal(false);
  showForm = signal(false);
  editando = signal<AtividadeRemota | null>(null);

  /** Espelha o formulário para os campos condicionais reagirem à mudança. */
  private formValue = signal<Record<string, unknown>>({});

  readonly colunas = ['aluno', 'rgm', 'registro', 'entrega'];

  readonly tiposTarefa: { valor: TipoTarefaRemota; rotulo: string }[] = [
    { valor: 'questionario', rotulo: 'Questionário' },
    { valor: 'arquivo', rotulo: 'Envio de arquivo' },
    { valor: 'discursiva', rotulo: 'Resposta discursiva' },
    { valor: 'estudo_de_caso', rotulo: 'Estudo de caso' },
    { valor: 'aula_online', rotulo: 'Participação em aula on-line' },
    { valor: 'leitura', rotulo: 'Confirmação de leitura' },
    { valor: 'formulario', rotulo: 'Formulário de avaliação' }
  ];

  form = this.fb.group({
    title: ['', Validators.required],
    description: [''],
    groupId: ['', Validators.required],
    scheduleId: [''],
    activityDate: ['', Validators.required],
    startTime: ['08:00', Validators.required],
    endTime: ['12:00', Validators.required],
    estimatedHours: [4, [Validators.required, Validators.min(0.5), Validators.max(24)]],
    requiresTask: [false],
    taskType: [''],
    taskInstructions: ['']
  });

  /** O aluno vê a agenda; professor e coordenadora veem a gestão das atividades. */
  ehAluno = computed(() => this.auth.role() === 'aluno');
  /** A coordenadora acompanha, mas não altera — a API bloqueia do mesmo jeito. */
  podeEditar = this.auth.ehProfessor;

  /** A tarefa complementar só é configurada quando a atividade a exige. */
  exigeTarefa = computed(() => this.formValue()['requiresTask'] === true);

  /** Rodízios da turma escolhida — vincular a atividade ao rodízio é opcional. */
  rodiziosDaTurma = computed<RotationSchedule[]>(() => {
    const groupId = this.formValue()['groupId'] as string | undefined;
    return groupId ? this.schedules().filter(s => s.groupId === groupId) : [];
  });

  constructor(
    private service: AtividadesRemotasService,
    private groupsService: GroupsService,
    private auth: AuthService,
    private snackBar: MatSnackBar,
    private fb: FormBuilder
  ) {}

  ngOnInit(): void {
    this.form.valueChanges.subscribe(v => this.formValue.set(v as Record<string, unknown>));
    this.load();
  }

  load(): void {
    this.loading.set(true);

    if (this.ehAluno()) {
      this.service.minhas().subscribe({
        next: (a) => { this.minhas.set(a); this.loading.set(false); },
        error: () => this.loading.set(false)
      });
      return;
    }

    this.groupsService.getAll().subscribe(g => this.groups.set(g));
    this.groupsService.getSchedules().subscribe(s => this.schedules.set(s));
    this.service.getAll().subscribe({
      next: (a) => { this.atividades.set(a); this.loading.set(false); },
      error: () => this.loading.set(false)
    });
  }

  // ── Formulário ────────────────────────────────────────────────────────────
  novo(): void {
    this.editando.set(null);
    this.form.reset({
      title: '', description: '', groupId: '', scheduleId: '',
      activityDate: hojeIso(),
      startTime: '08:00', endTime: '12:00', estimatedHours: 4,
      requiresTask: false, taskType: '', taskInstructions: ''
    });
    this.showForm.set(true);
  }

  editar(a: AtividadeRemota): void {
    this.editando.set(a);
    this.form.reset({
      title: a.title,
      description: a.description ?? '',
      groupId: a.groupId,
      scheduleId: a.scheduleId ?? '',
      activityDate: a.activityDate.substring(0, 10),
      startTime: a.startTime.substring(0, 5),
      endTime: a.endTime.substring(0, 5),
      estimatedHours: a.estimatedHours,
      requiresTask: a.requiresTask,
      taskType: a.taskType ?? '',
      taskInstructions: a.taskInstructions ?? ''
    });
    this.showForm.set(true);
  }

  /** Nome dos campos como aparecem na tela, para o aviso dizer o que corrigir. */
  private readonly rotulosCampos: Record<string, string> = {
    title: 'Título',
    groupId: 'Turma ou grupo autorizado',
    activityDate: 'Data',
    startTime: 'Início',
    endTime: 'Término',
    estimatedHours: 'Carga horária',
    taskType: 'Tipo da tarefa',
    taskInstructions: 'Instruções da tarefa'
  };

  /** Mensagem do servidor para o campo, exibida logo abaixo dele. */
  erroServidor(campo: string): string | null {
    return this.form.get(campo)?.getError('servidor') ?? null;
  }

  salvar(): void {
    // Com a tarefa complementar ativa, tipo e instruções passam a ser obrigatórios.
    const exigeTarefa = this.form.value.requiresTask === true;
    this.exigirSe('taskType', exigeTarefa);
    this.exigirSe('taskInstructions', exigeTarefa);

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      this.snackBar.open(this.resumoCamposInvalidos(), 'OK',
        { duration: 6000, panelClass: 'snack-error' });
      return;
    }

    const v = this.form.value;
    if (v.endTime! <= v.startTime!) {
      this.marcarErro('endTime', 'O horário de término precisa ser posterior ao de início.');
      return;
    }

    const dto: AtividadeRemotaForm = {
      title: v.title!,
      description: v.description || undefined,
      groupId: v.groupId!,
      scheduleId: v.scheduleId || undefined,
      activityDate: v.activityDate!,
      startTime: v.startTime!,
      endTime: v.endTime!,
      estimatedHours: Number(v.estimatedHours),
      requiresTask: !!v.requiresTask,
      taskType: (v.taskType || undefined) as TipoTarefaRemota | undefined,
      taskInstructions: v.taskInstructions || undefined
    };

    this.saving.set(true);
    const atual = this.editando();
    const op = atual ? this.service.update(atual.id, dto) : this.service.create(dto);

    op.subscribe({
      next: (a) => {
        this.saving.set(false);
        this.showForm.set(false);
        this.snackBar.open(
          atual ? 'Atividade atualizada.' : `Atividade criada. Código de presença: ${a.presenceCode}`,
          'OK', { duration: 8000, panelClass: 'snack-success' });
        this.load();
      },
      error: (err) => {
        this.saving.set(false);
        // O campo recusado pelo servidor ganha borda vermelha e o motivo abaixo dele.
        aplicarErrosServidor(this.form, err);
        this.snackBar.open(mensagemErro(err, 'Erro ao salvar a atividade.'), 'OK',
          { duration: 8000, panelClass: 'snack-error' });
      }
    });
  }

  /** Liga ou desliga a obrigatoriedade de um campo conforme o resto do formulário. */
  private exigirSe(campo: string, obrigatorio: boolean): void {
    const controle = this.form.get(campo)!;
    controle.setValidators(obrigatorio
      ? [c => (c.value ?? '').toString().trim() ? null : { required: true }]
      : null);
    controle.updateValueAndValidity({ emitEvent: false });
  }

  /** Destaca o campo com o motivo e avisa, em vez de só mostrar um toast genérico. */
  private marcarErro(campo: string, mensagem: string): void {
    const controle = this.form.get(campo);
    controle?.setErrors({ ...(controle.errors ?? {}), servidor: mensagem });
    controle?.markAsTouched();
    this.snackBar.open(`Erro ao salvar: ${mensagem}`, 'OK',
      { duration: 6000, panelClass: 'snack-error' });
  }

  /** Diz quais campos impedem o salvamento, em vez de "preencha os obrigatórios". */
  private resumoCamposInvalidos(): string {
    const motivos = Object.entries(this.form.controls)
      .filter(([, c]) => c.invalid)
      .map(([nome, c]) => {
        const rotulo = this.rotulosCampos[nome] ?? nome;
        if (nome === 'taskInstructions')
          return "O campo 'Instruções da tarefa' é obrigatório quando a tarefa complementar está ativa.";
        return c.hasError('required')
          ? `O campo '${rotulo}' é obrigatório.`
          : `O campo '${rotulo}' está com um valor inválido.`;
      });

    return `Erro ao salvar: ${motivos.slice(0, 2).join(' ')}`;
  }

  // ── Ações do professor ────────────────────────────────────────────────────
  encerrar(a: AtividadeRemota): void {
    if (!confirm(`Encerrar "${a.title}"? O código deixa de valer imediatamente; as participações já registradas permanecem.`)) return;
    this.service.encerrar(a.id).subscribe({
      next: () => { this.snackBar.open('Atividade encerrada.', '', { duration: 3000 }); this.load(); },
      error: (err) => this.snackBar.open(mensagemErro(err, 'Erro ao encerrar.'), '',
        { duration: 4000, panelClass: 'snack-error' })
    });
  }

  novoCodigo(a: AtividadeRemota): void {
    if (!confirm(`Gerar um novo código para "${a.title}"? O código atual deixa de funcionar.`)) return;
    this.service.novoCodigo(a.id).subscribe({
      next: (nova) => {
        this.snackBar.open(`Novo código: ${nova.presenceCode}`, 'OK',
          { duration: 8000, panelClass: 'snack-success' });
        this.load();
      },
      error: (err) => this.snackBar.open(mensagemErro(err, 'Erro ao gerar o código.'), '',
        { duration: 4000, panelClass: 'snack-error' })
    });
  }

  excluir(a: AtividadeRemota): void {
    if (!confirm(`Excluir "${a.title}"?`)) return;
    this.service.delete(a.id).subscribe({
      next: () => { this.snackBar.open('Atividade excluída.', '', { duration: 3000 }); this.load(); },
      error: (err) => this.snackBar.open(mensagemErro(err, 'Erro ao excluir.'), 'OK',
        { duration: 6000, panelClass: 'snack-error' })
    });
  }

  /** Copia o código para o professor colar no canal em que combina com a turma. */
  copiarCodigo(a: AtividadeRemota): void {
    navigator.clipboard?.writeText(a.presenceCode).then(
      () => this.snackBar.open(`Código ${a.presenceCode} copiado.`, '', { duration: 2500 }),
      () => this.snackBar.open(`Código: ${a.presenceCode}`, 'OK', { duration: 6000 })
    );
  }

  carregarParticipacoes(a: AtividadeRemota): void {
    if (this.participacoes()[a.id]) return;
    this.service.getParticipacoes(a.id).subscribe(p =>
      this.participacoes.update(cur => ({ ...cur, [a.id]: p })));
  }

  participantes(id: string): ParticipacaoAtividade[] {
    return this.participacoes()[id] ?? [];
  }
}
