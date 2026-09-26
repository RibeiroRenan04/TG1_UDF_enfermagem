import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatTableModule } from '@angular/material/table';
import { MatSortModule } from '@angular/material/sort';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { GroupsService, RotationScheduleInput } from '../../core/services/groups.service';
import { LocationsService } from '../../core/services/locations.service';
import { UsersService } from '../../core/services/users.service';
import {
  StudentGroup, RotationSchedule, Location, GroupMember, UserDto, ModoAtividade
} from '../../core/models/models';
import {
  VincularAlunosDialogComponent,
  VincularAlunosResult
} from '../usuarios/vincular-alunos-dialog.component';
import { aplicarErrosServidor, mensagemErro } from '../../core/utils/api-error';
import { normalizarBusca } from '../../core/utils/paginacao';
import { Ordenacao } from '../../core/utils/ordenacao';
import { CAMPOS_TIPADOS } from '../../core/utils/campos.directive';
import { rotuloTurno } from '../../core/utils/turma';

const DIA_SEXTA = 5;

/**
 * Linha da programação semanal na tela. `ativo` separa o dia que faz parte do
 * rodízio daquele sem atividade nenhuma; só os ativos vão para a API.
 */
interface LinhaDia {
  dayOfWeek: number;
  label: string;
  ativo: boolean;
  mode: ModoAtividade;
  /** Vazio herda o local principal do rodízio. */
  locationId: string;
}

export const TURNOS_RODIZIO = [
  { valor: 'manha', rotulo: 'Manhã' },
  { valor: 'tarde', rotulo: 'Tarde' },
  { valor: 'noite', rotulo: 'Noite' }
];

export const ATIVIDADES_RODIZIO = [
  { valor: 'assistencia', rotulo: 'Assistência' },
  { valor: 'gestao', rotulo: 'Gestão' },
  { valor: 'pic', rotulo: 'Práticas Integrativas (PIC)' },
  { valor: 'outro', rotulo: 'Outra atividade' }
];

/**
 * Página de uma turma: alunos vinculados, rodízios alocados e o formulário de
 * alocação. Antes tudo isso ficava dentro de um painel expansível da lista de
 * turmas, espremido no celular; numa página própria há espaço para gerenciar —
 * e o voltar do aparelho retorna à lista.
 */
@Component({
  selector: 'app-turma-detalhe',
  standalone: true,
  imports: [
    CommonModule, RouterLink, ReactiveFormsModule, MatDatepickerModule,
    MatCardModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule,
    MatSelectModule, MatTableModule, MatSortModule, MatProgressSpinnerModule,
    MatSnackBarModule, MatTooltipModule, MatDialogModule, MatCheckboxModule,
    ...CAMPOS_TIPADOS
  ],
  templateUrl: './turma-detalhe.component.html',
  styleUrls: ['./rodizios.component.scss', './turma-detalhe.component.scss']
})
export class TurmaDetalheComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly fb = inject(FormBuilder);

  readonly groupId = this.route.snapshot.paramMap.get('id') ?? '';

  group = signal<StudentGroup | null>(null);
  naoEncontrada = signal(false);
  loading = signal(true);
  membros = signal<GroupMember[]>([]);
  carregandoMembros = signal(true);
  locations = signal<Location[]>([]);
  preceptors = signal<UserDto[]>([]);
  /** Todas as escalas, para detectar preceptor escalado em dois locais ao mesmo tempo. */
  allSchedules = signal<RotationSchedule[]>([]);
  escalas = computed(() => this.allSchedules()
    .filter(s => s.groupId === this.groupId)
    .sort((a, b) => (a.startDate ?? '').localeCompare(b.startDate ?? '')));

  showScheduleForm = signal(false);
  savingSchedule = signal(false);
  editingSchedule = signal<RotationSchedule | null>(null);

  readonly turnos = TURNOS_RODIZIO;
  readonly atividades = ATIVIDADES_RODIZIO;
  readonly modos: { valor: ModoAtividade; rotulo: string }[] = [
    { valor: 'presencial', rotulo: 'Presencial' },
    { valor: 'remoto', rotulo: 'Atividade remota' }
  ];

  // ── Alunos vinculados ───────────────────────────────────────────────────────
  readonly buscaAluno = signal('');
  readonly membrosFiltrados = computed(() => {
    const termo = normalizarBusca(this.buscaAluno());
    return termo
      ? this.membros().filter(m => [m.fullName, m.rgm].some(v => normalizarBusca(v).includes(termo)))
      : this.membros();
  });
  readonly ordenacaoAlunos = new Ordenacao(this.membrosFiltrados, {
    nome: m => m.fullName,
    turno: m => rotuloTurno(m.shift)
  }, { active: 'nome', direction: 'asc' });
  readonly colunasAlunos = ['nome', 'rgm', 'semester', 'turno'];

  /**
   * Programação semanal em edição. Preenchida, o sistema gera sozinho a
   * programação de cada data do período — não é preciso cadastrar dia a dia.
   * Deixada inteira desmarcada, o rodízio segue no padrão: todo dia útil
   * presencial no local principal.
   */
  dias = signal<LinhaDia[]>([]);
  temProgramacaoSemanal = computed(() => this.dias().some(d => d.ativo));

  /** Às sextas o estágio presencial é sempre na UDF (Laboratórios de Enfermagem) — a API aplica o mesmo. */
  readonly localSexta = computed<Location | null>(() => this.locations()
    .filter(l => l.isInstitution)
    .sort((a, b) => a.name.localeCompare(b.name))[0] ?? null);

  readonly buscaLocal = signal('');

  /** Espelha o formulário de alocação para o aviso de conflito reagir a mudanças. */
  private formValue = signal<Partial<Record<string, unknown>>>({});

  /** Alocação do rodízio da turma, preenchida pelo supervisor. */
  scheduleForm = this.fb.group({
    groupId: ['', Validators.required],
    shift: ['manha', Validators.required],
    periodLabel: ['', [Validators.required, Validators.maxLength(100)]],
    locationId: ['', Validators.required],
    preceptorId: ['', Validators.required],
    startDate: ['', Validators.required],
    endDate: ['', Validators.required],
    activityType: ['assistencia', Validators.required],
    requiredHours: [80, [Validators.required, Validators.min(1), Validators.max(2000)]],
    notes: ['']
  });

  /** Erro do servidor que não corresponde a um campo (ex.: programação semanal). */
  erroGeral = signal<string | null>(null);

  constructor(
    private groupsService: GroupsService,
    private locationsService: LocationsService,
    private usersService: UsersService,
    private snackBar: MatSnackBar,
    private dialog: MatDialog
  ) {}

  /**
   * Preceptor já escalado em outro rodízio no mesmo turno com datas sobrepostas.
   * O backend permite essa situação — aqui é só um alerta para o supervisor
   * confirmar se o preceptor realmente cobre os dois locais.
   */
  conflitoPreceptor = computed<RotationSchedule | null>(() => {
    const v = this.formValue();
    const preceptorId = v['preceptorId'] as string | undefined;
    const shift       = v['shift'] as string | undefined;
    const startDate   = v['startDate'] as string | undefined;
    const endDate     = v['endDate'] as string | undefined;
    if (!preceptorId || !shift || !startDate || !endDate) return null;

    const emEdicao = this.editingSchedule()?.id;
    return this.allSchedules().find(s =>
      s.preceptorId === preceptorId &&
      s.id !== emEdicao &&
      s.shift === shift &&
      (s.startDate ?? '').substring(0, 10) <= endDate &&
      (s.endDate ?? '').substring(0, 10) >= startDate
    ) ?? null;
  });

  ngOnInit(): void {
    this.locationsService.getAll().subscribe(l => this.locations.set(l));
    this.loadSchedules();
    this.scheduleForm.valueChanges.subscribe(v => this.formValue.set(v as Record<string, unknown>));
    // Apenas preceptores: são eles que realizam o acompanhamento dos alunos alocados.
    this.usersService.getPreceptors().subscribe(p =>
      this.preceptors.set(p.filter(u => u.role === 'preceptor' && u.isActive !== false))
    );
    this.loadGroup();
    this.loadMembers();
  }

  loadGroup(): void {
    this.groupsService.getAll().subscribe({
      next: (g) => {
        const turma = g.find(t => t.id === this.groupId) ?? null;
        this.group.set(turma);
        this.naoEncontrada.set(!turma);
        this.loading.set(false);
      },
      error: (err) => {
        this.loading.set(false);
        this.snackBar.open(mensagemErro(err, 'Erro ao carregar a turma'), '', { duration: 4000, panelClass: 'snack-error' });
      }
    });
  }

  loadSchedules(): void {
    this.groupsService.getSchedules().subscribe(s => this.allSchedules.set(s));
  }

  loadMembers(): void {
    this.carregandoMembros.set(true);
    this.groupsService.getMembers(this.groupId).subscribe({
      next: (m) => { this.membros.set(m); this.carregandoMembros.set(false); },
      error: () => this.carregandoMembros.set(false)
    });
  }

  /** A turma só pode receber rodízio depois que os alunos forem vinculados. */
  temAlunosVinculados(): boolean {
    return this.carregandoMembros() ? (this.group()?.memberCount ?? 0) > 0 : this.membros().length > 0;
  }

  // ── Turma ─────────────────────────────────────────────────────────────────
  deleteGroup(): void {
    const g = this.group();
    if (!g) return;
    if (!confirm(`Excluir a turma ${g.code}? As escalas de rodízio vinculadas também serão removidas.`)) return;
    this.groupsService.delete(g.id).subscribe({
      next: () => {
        this.snackBar.open('Turma excluída.', '', { duration: 2500, panelClass: 'snack-success' });
        this.router.navigate(['/app/rodizios'], { replaceUrl: true });
      },
      error: (err) => this.snackBar.open(mensagemErro(err, 'Erro ao excluir'), '', { duration: 3000, panelClass: 'snack-error' })
    });
  }

  /**
   * Monta a turma. É o passo que faltava para liberar a alocação: sem vínculo,
   * o backend recusa o rodízio e o aluno não consegue fazer check-in.
   */
  vincularAlunos(): void {
    const group = this.group();
    if (!group) return;
    const ref = this.dialog.open(VincularAlunosDialogComponent, {
      width: 'min(720px, 94vw)',
      data: { group }
    });

    ref.afterClosed().subscribe((res: VincularAlunosResult | null) => {
      if (!res) return;
      this.snackBar.open(
        `${res.vinculados} aluno(s) vinculado(s), ${res.desvinculados} desvinculado(s).`,
        '', { duration: 3000, panelClass: 'snack-success' });
      this.loadMembers();
      this.loadGroup();
    });
  }

  // ── Alocação de rodízio ───────────────────────────────────────────────────
  /**
   * Linhas de segunda a sábado. O sábado atende cursos com plantões, práticas
   * supervisionadas e aulas de campo; como os demais dias, só vale se marcado.
   */
  private linhasPadrao(): LinhaDia[] {
    const rotulos = ['Segunda-feira', 'Terça-feira', 'Quarta-feira', 'Quinta-feira', 'Sexta-feira', 'Sábado'];
    return rotulos.map((label, i) => ({
      dayOfWeek: i + 1, label, ativo: false, mode: 'presencial' as ModoAtividade, locationId: ''
    }));
  }

  /** Reidrata a programação salva sobre as linhas da semana. */
  private linhasDe(schedule: RotationSchedule): LinhaDia[] {
    return this.linhasPadrao().map(linha => {
      const dia = schedule.days?.find(d => d.dayOfWeek === linha.dayOfWeek);
      if (!dia) return linha;
      return {
        ...linha,
        ativo: true,
        mode: dia.mode,
        // O local só é reidratado quando difere do principal: assim a tela mostra
        // "herda do rodízio" no caso comum, em vez de repetir a unidade.
        locationId: dia.locationId && dia.locationId !== schedule.locationId ? dia.locationId : ''
      };
    });
  }

  /** Opções do campo Local filtradas pela busca, sem esconder o local já escolhido. */
  locaisFiltrados(selecionado?: string | null): Location[] {
    const termo = normalizarBusca(this.buscaLocal());
    if (!termo) return this.locations();
    return this.locations().filter(l => l.id === selecionado || normalizarBusca(l.name).includes(termo));
  }

  ehSexta(d: LinhaDia): boolean {
    return d.dayOfWeek === DIA_SEXTA;
  }

  modosDoDia(d: LinhaDia) {
    return this.ehSexta(d) ? this.modos.filter(m => m.valor !== 'sem_atividade') : this.modos;
  }

  atualizarDia(dayOfWeek: number, mudanca: Partial<LinhaDia>): void {
    this.dias.update(atual => atual.map(d =>
      d.dayOfWeek === dayOfWeek ? { ...d, ...mudanca } : d));
  }

  /**
   * Marca segunda a sexta como presencial no local principal — o caso mais comum.
   * O sábado fica de fora: ele é a exceção e precisa ser marcado a propósito.
   */
  marcarTodosPresenciais(): void {
    this.dias.update(atual => atual.map(d => d.dayOfWeek <= 5
      ? { ...d, ativo: true, mode: 'presencial' as ModoAtividade }
      : d));
  }

  limparProgramacao(): void {
    this.dias.set(this.linhasPadrao());
  }

  /** Mensagem do servidor para o campo, exibida logo abaixo dele. */
  erroServidor(campo: string): string | null {
    return this.scheduleForm.get(campo)?.getError('servidor') ?? null;
  }

  /** Destaca o campo com o motivo e avisa, em vez de só mostrar um toast genérico. */
  private marcarErro(campo: string, mensagem: string): void {
    const controle = this.scheduleForm.get(campo);
    controle?.setErrors({ ...(controle.errors ?? {}), servidor: mensagem });
    controle?.markAsTouched();
    this.snackBar.open(`Erro ao salvar alocação: ${mensagem}`, 'OK',
      { duration: 6000, panelClass: 'snack-error' });
  }

  abrirAlocacao(): void {
    const group = this.group();
    if (!group) return;
    if (!this.temAlunosVinculados()) {
      this.snackBar.open(
        `A turma ${group.code} não possui alunos vinculados. Use "Vincular alunos" antes de alocar o rodízio.`,
        'OK', { duration: 6000, panelClass: 'snack-error' });
      return;
    }
    this.editingSchedule.set(null);
    this.scheduleForm.reset({
      groupId: group.id,
      shift: 'manha',
      periodLabel: '',
      locationId: '',
      preceptorId: '',
      startDate: '',
      endDate: '',
      activityType: 'assistencia',
      requiredHours: 80,
      notes: ''
    });
    this.dias.set(this.linhasPadrao());
    this.erroGeral.set(null);
    this.showScheduleForm.set(true);
    this.rolarParaFormulario();
  }

  editarAlocacao(s: RotationSchedule): void {
    this.editingSchedule.set(s);
    this.scheduleForm.reset({
      groupId: s.groupId,
      shift: s.shift,
      periodLabel: s.periodLabel,
      locationId: s.locationId,
      preceptorId: s.preceptorId ?? '',
      startDate: s.startDate?.substring(0, 10) ?? '',
      endDate: s.endDate?.substring(0, 10) ?? '',
      activityType: s.activityType,
      requiredHours: s.requiredHours,
      notes: s.notes ?? ''
    });
    this.dias.set(this.linhasDe(s));
    this.erroGeral.set(null);
    this.showScheduleForm.set(true);
    this.rolarParaFormulario();
  }

  /** No celular o formulário abre abaixo da lista: leva o usuário até ele. */
  private rolarParaFormulario(): void {
    setTimeout(() => document.getElementById('form-alocacao')
      ?.scrollIntoView({ behavior: 'smooth', block: 'start' }));
  }

  salvarAlocacao(): void {
    // Clique duplo no botão disparava duas gravações quase simultâneas, e a
    // segunda voltava como "o registro foi alterado por outra pessoa". O botão
    // já fica desabilitado enquanto salva; esta trava cobre o intervalo entre os
    // dois cliques, antes de a tela se atualizar.
    if (this.savingSchedule()) return;

    this.erroGeral.set(null);

    if (this.scheduleForm.invalid) {
      this.scheduleForm.markAllAsTouched();
      this.snackBar.open('Preencha todos os campos obrigatórios da alocação.', '', { duration: 3000, panelClass: 'snack-error' });
      return;
    }

    const v = this.scheduleForm.value;
    if (v.endDate! < v.startDate!) {
      this.marcarErro('endDate', 'A data de término não pode ser anterior à data de início.');
      return;
    }

    // Ano digitado errado (2004 no lugar de 2026) passava direto e o painel do
    // aluno acabava contando anos inteiros de "dias sem registro".
    const anoInvalido = (data: string) => {
      const ano = Number(data.substring(0, 4));
      return !ano || ano < 2020 || ano > 2100;
    };
    if (anoInvalido(v.startDate!)) {
      this.marcarErro('startDate', 'Data de início inválida. Confira o ano informado.');
      return;
    }
    if (anoInvalido(v.endDate!)) {
      this.marcarErro('endDate', 'Data de término inválida. Confira o ano informado.');
      return;
    }

    this.savingSchedule.set(true);
    const dto = {
      groupId: v.groupId!,
      locationId: v.locationId!,
      preceptorId: v.preceptorId!,
      shift: v.shift!,
      periodLabel: v.periodLabel!,
      startDate: v.startDate!,
      endDate: v.endDate!,
      activityType: v.activityType!,
      requiredHours: Number(v.requiredHours),
      notes: v.notes || undefined,
      // Só os dias marcados viram programação. Lista vazia devolve o rodízio ao
      // padrão — todo dia útil presencial no local principal.
      days: this.dias()
        .filter(d => d.ativo)
        .map(d => ({
          dayOfWeek: d.dayOfWeek,
          mode: d.mode,
          locationId: d.mode !== 'presencial' ? undefined
            : d.dayOfWeek === DIA_SEXTA ? this.localSexta()?.id
            : d.locationId || undefined
        }))
    } satisfies RotationScheduleInput;

    const atual = this.editingSchedule();
    const op = atual
      ? this.groupsService.updateSchedule(atual.id, dto)
      : this.groupsService.createSchedule(dto);

    op.subscribe({
      next: () => {
        this.savingSchedule.set(false);
        this.snackBar.open(atual ? 'Alocação atualizada!' : 'Rodízio alocado!', '', { duration: 2500, panelClass: 'snack-success' });
        this.showScheduleForm.set(false);
        this.loadSchedules();
      },
      error: (err) => {
        this.savingSchedule.set(false);
        const motivo = mensagemErro(err, 'Erro ao salvar alocação');
        // Campo recusado ganha borda vermelha e o motivo abaixo dele; o que não
        // tem campo correspondente (programação semanal) vai para o aviso do topo.
        if (!aplicarErrosServidor(this.scheduleForm, err)) this.erroGeral.set(motivo);
        this.snackBar.open(motivo, 'OK', { duration: 8000, panelClass: 'snack-error' });
      }
    });
  }

  excluirAlocacao(s: RotationSchedule): void {
    if (!confirm(`Excluir o rodízio de ${s.periodLabel} em ${s.locationName}?`)) return;
    this.groupsService.deleteSchedule(s.id).subscribe({
      next: () => {
        this.snackBar.open('Alocação removida.', '', { duration: 2000 });
        this.loadSchedules();
      },
      error: (err) => this.snackBar.open(mensagemErro(err, 'Erro ao excluir alocação'), '', { duration: 3000, panelClass: 'snack-error' })
    });
  }

  // ── Rótulos ───────────────────────────────────────────────────────────────
  turnoLabel(valor?: string | null): string {
    return rotuloTurno(valor) || '—';
  }

  atividadeLabel(valor: string): string {
    return this.atividades.find(a => a.valor === valor)?.rotulo ?? valor;
  }
}
