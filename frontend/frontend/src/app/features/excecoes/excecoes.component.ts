import { Component, OnInit, computed, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatTableModule } from '@angular/material/table';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { GroupsService } from '../../core/services/groups.service';
import { LocationsService } from '../../core/services/locations.service';
import { UsersService } from '../../core/services/users.service';
import { AuthService } from '../../core/services/auth.service';
import {
  ExcecoesCalendarioService, ExcecaoCalendarioForm
} from '../../core/services/excecoes-calendario.service';
import {
  AbrangenciaExcecao, ExcecaoCalendario, Location, RotationSchedule,
  StudentGroup, TipoExcecao, UserDto
} from '../../core/models/models';

/**
 * Calendário de exceções.
 *
 * Um feriado, um recesso ou uma unidade que ficou indisponível são cadastrados
 * uma única vez, com a abrangência certa. A programação de cada dia aplica a
 * exceção sozinha — não é preciso alterar aluno por aluno.
 */
@Component({
  selector: 'app-excecoes',
  standalone: true,
  imports: [
    CommonModule, ReactiveFormsModule,
    MatCardModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule,
    MatSelectModule, MatTableModule, MatProgressSpinnerModule, MatSnackBarModule,
    MatTooltipModule
  ],
  templateUrl: './excecoes.component.html',
  styleUrls: ['./excecoes.component.scss']
})
export class ExcecoesComponent implements OnInit {
  excecoes = signal<ExcecaoCalendario[]>([]);
  groups = signal<StudentGroup[]>([]);
  schedules = signal<RotationSchedule[]>([]);
  locations = signal<Location[]>([]);
  students = signal<UserDto[]>([]);
  /** Cursos já cadastrados nos alunos — evita o mesmo curso escrito de dois jeitos. */
  cursos = signal<string[]>([]);
  loading = signal(true);
  saving = signal(false);
  showForm = signal(false);
  editando = signal<ExcecaoCalendario | null>(null);

  /** Espelha o formulário para os campos condicionais reagirem à mudança. */
  private formValue = signal<Record<string, unknown>>({});

  readonly colunas = ['periodo', 'tipo', 'abrangencia', 'alvo', 'descricao', 'acoes'];

  readonly tipos: { valor: TipoExcecao; rotulo: string; dispensa: boolean }[] = [
    { valor: 'feriado', rotulo: 'Feriado', dispensa: true },
    { valor: 'recesso', rotulo: 'Recesso', dispensa: true },
    { valor: 'cancelado', rotulo: 'Estágio cancelado', dispensa: true },
    { valor: 'remoto', rotulo: 'Atividade remota', dispensa: false },
    { valor: 'troca_local', rotulo: 'Troca de local', dispensa: false },
    { valor: 'atividade_especial', rotulo: 'Atividade especial', dispensa: false },
    { valor: 'reposicao', rotulo: 'Reposição de atividade', dispensa: false }
  ];

  readonly abrangencias: { valor: AbrangenciaExcecao; rotulo: string }[] = [
    { valor: 'faculdade', rotulo: 'Toda a faculdade' },
    { valor: 'curso', rotulo: 'Curso' },
    { valor: 'turma', rotulo: 'Turma' },
    { valor: 'rodizio', rotulo: 'Rodízio específico' },
    { valor: 'aluno', rotulo: 'Aluno específico' }
  ];

  readonly turnos = [
    { valor: '', rotulo: 'Todos os turnos' },
    { valor: 'manha', rotulo: 'Manhã' },
    { valor: 'tarde', rotulo: 'Tarde' },
    { valor: 'noite', rotulo: 'Noite' }
  ];

  form = this.fb.group({
    type: ['feriado' as TipoExcecao, Validators.required],
    scope: ['faculdade' as AbrangenciaExcecao, Validators.required],
    startDate: ['', Validators.required],
    endDate: [''],
    shift: [''],
    groupId: [''],
    scheduleId: [''],
    studentId: [''],
    course: [''],
    locationId: [''],
    description: ['', Validators.required]
  });

  podeEditar = this.auth.ehProfessor;

  private tipoAtual = computed(() => this.formValue()['type'] as TipoExcecao | undefined);
  private escopoAtual = computed(() => this.formValue()['scope'] as AbrangenciaExcecao | undefined);

  /** Cada abrangência pede o seu alvo; sem ele a exceção alcançaria gente demais. */
  pedeTurma = computed(() => this.escopoAtual() === 'turma');
  pedeRodizio = computed(() => this.escopoAtual() === 'rodizio');
  pedeAluno = computed(() => this.escopoAtual() === 'aluno');
  pedeCurso = computed(() => this.escopoAtual() === 'curso');

  /** A troca de local exige o destino; os demais tipos aceitam um local opcional. */
  pedeLocal = computed(() => this.tipoAtual() === 'troca_local');
  aceitaLocal = computed(() =>
    this.tipoAtual() === 'atividade_especial' || this.tipoAtual() === 'reposicao');

  /** Aviso da tela: este tipo dispensa o ponto no período. */
  dispensaPonto = computed(() =>
    this.tipos.find(t => t.valor === this.tipoAtual())?.dispensa === true);

  constructor(
    private service: ExcecoesCalendarioService,
    private groupsService: GroupsService,
    private locationsService: LocationsService,
    private usersService: UsersService,
    private auth: AuthService,
    private snackBar: MatSnackBar,
    private fb: FormBuilder
  ) {}

  ngOnInit(): void {
    this.form.valueChanges.subscribe(v => this.formValue.set(v as Record<string, unknown>));
    this.groupsService.getAll().subscribe(g => this.groups.set(g));
    this.groupsService.getSchedules().subscribe(s => this.schedules.set(s));
    this.locationsService.getAll().subscribe(l => this.locations.set(l));
    this.usersService.getStudents().subscribe(s => this.students.set(s));
    this.usersService.getCursos().subscribe(c => this.cursos.set(c));
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.service.getAll().subscribe({
      next: (e) => { this.excecoes.set(e); this.loading.set(false); },
      error: () => this.loading.set(false)
    });
  }

  /** Alvo da exceção em uma linha, para a coluna da tabela. */
  alvo(e: ExcecaoCalendario): string {
    switch (e.scope) {
      case 'turma': return e.groupCode ?? '—';
      case 'rodizio': return e.periodLabel ?? '—';
      case 'aluno': return e.studentName ?? '—';
      case 'curso': return e.course ?? '—';
      default: return 'Todos';
    }
  }

  novo(): void {
    this.editando.set(null);
    this.form.reset({
      type: 'feriado', scope: 'faculdade',
      startDate: new Date().toISOString().substring(0, 10),
      endDate: '', shift: '', groupId: '', scheduleId: '', studentId: '',
      course: '', locationId: '', description: ''
    });
    this.showForm.set(true);
  }

  editar(e: ExcecaoCalendario): void {
    this.editando.set(e);
    this.form.reset({
      type: e.type,
      scope: e.scope,
      startDate: e.startDate.substring(0, 10),
      endDate: e.endDate.substring(0, 10),
      shift: e.shift ?? '',
      groupId: e.groupId ?? '',
      scheduleId: e.scheduleId ?? '',
      studentId: e.studentId ?? '',
      course: e.course ?? '',
      locationId: e.locationId ?? '',
      description: e.description
    });
    this.showForm.set(true);
  }

  salvar(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      this.snackBar.open('Preencha os campos obrigatórios da exceção.', '',
        { duration: 3000, panelClass: 'snack-error' });
      return;
    }

    const v = this.form.value;
    const erro = this.validarAlvo();
    if (erro) {
      this.snackBar.open(erro, '', { duration: 4000, panelClass: 'snack-error' });
      return;
    }

    const dto: ExcecaoCalendarioForm = {
      type: v.type as TipoExcecao,
      scope: v.scope as AbrangenciaExcecao,
      startDate: v.startDate!,
      endDate: v.endDate || undefined,
      shift: (v.shift || undefined) as ExcecaoCalendarioForm['shift'],
      groupId: v.scope === 'turma' ? v.groupId! : undefined,
      scheduleId: v.scope === 'rodizio' ? v.scheduleId! : undefined,
      studentId: v.scope === 'aluno' ? v.studentId! : undefined,
      course: v.scope === 'curso' ? v.course! : undefined,
      locationId: v.locationId || undefined,
      description: v.description!
    };

    this.saving.set(true);
    const atual = this.editando();
    const op = atual ? this.service.update(atual.id, dto) : this.service.create(dto);

    op.subscribe({
      next: () => {
        this.saving.set(false);
        this.showForm.set(false);
        this.snackBar.open(atual ? 'Exceção atualizada.' : 'Exceção cadastrada.', '',
          { duration: 3000, panelClass: 'snack-success' });
        this.load();
      },
      error: (err) => {
        this.saving.set(false);
        this.snackBar.open(err?.error?.message ?? 'Erro ao salvar a exceção.', 'OK',
          { duration: 6000, panelClass: 'snack-error' });
      }
    });
  }

  /** A mesma conferência que a API faz, antecipada para poupar o ida e volta. */
  private validarAlvo(): string | null {
    const v = this.form.value;
    if (v.endDate && v.endDate < v.startDate!)
      return 'A data final não pode ser anterior à inicial.';
    if (v.scope === 'turma' && !v.groupId) return 'Selecione a turma alcançada.';
    if (v.scope === 'rodizio' && !v.scheduleId) return 'Selecione o rodízio alcançado.';
    if (v.scope === 'aluno' && !v.studentId) return 'Selecione o aluno alcançado.';
    if (v.scope === 'curso' && !v.course) return 'Informe o curso alcançado.';
    if (v.type === 'troca_local' && !v.locationId)
      return 'Selecione a unidade que passa a valer na troca de local.';
    return null;
  }

  excluir(e: ExcecaoCalendario): void {
    if (!confirm(`Excluir a exceção "${e.description}"?`)) return;
    this.service.delete(e.id).subscribe({
      next: () => { this.snackBar.open('Exceção removida.', '', { duration: 3000 }); this.load(); },
      error: () => this.snackBar.open('Erro ao excluir a exceção.', '',
        { duration: 4000, panelClass: 'snack-error' })
    });
  }
}
