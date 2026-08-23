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
import { MatExpansionModule } from '@angular/material/expansion';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatDividerModule } from '@angular/material/divider';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { GroupsService } from '../../core/services/groups.service';
import { LocationsService } from '../../core/services/locations.service';
import { UsersService } from '../../core/services/users.service';
import { StudentGroup, RotationSchedule, Location, GroupMember, UserDto } from '../../core/models/models';
import {
  VincularAlunosDialogComponent,
  VincularAlunosResult
} from '../usuarios/vincular-alunos-dialog.component';

@Component({
  selector: 'app-rodizios',
  standalone: true,
  imports: [
    CommonModule, ReactiveFormsModule,
    MatCardModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule,
    MatSelectModule, MatTableModule, MatExpansionModule, MatProgressSpinnerModule,
    MatSnackBarModule, MatTooltipModule, MatDividerModule, MatDialogModule
  ],
  templateUrl: './rodizios.component.html',
  styleUrls: ['./rodizios.component.scss']
})
export class RodiziosComponent implements OnInit {
  groups = signal<StudentGroup[]>([]);
  locations = signal<Location[]>([]);
  preceptors = signal<UserDto[]>([]);
  schedulesByGroup = signal<Record<string, RotationSchedule[]>>({});
  /** Todas as escalas, para detectar preceptor escalado em dois locais ao mesmo tempo. */
  allSchedules = signal<RotationSchedule[]>([]);
  /** Espelha o formulário de alocação para o aviso de conflito reagir a mudanças. */
  private formValue = signal<Partial<Record<string, unknown>>>({});
  membersByGroup = signal<Record<string, GroupMember[]>>({});
  loading = signal(true);
  showGroupForm = signal(false);
  showScheduleForm = signal(false);
  savingSchedule = signal(false);
  /** Turma alvo da alocação e escala em edição. */
  scheduleGroup = signal<StudentGroup | null>(null);
  editingSchedule = signal<RotationSchedule | null>(null);

  readonly turnos = [
    { valor: 'manha', rotulo: 'Manhã' },
    { valor: 'tarde', rotulo: 'Tarde' },
    { valor: 'noite', rotulo: 'Noite' }
  ];

  readonly atividades = [
    { valor: 'assistencia', rotulo: 'Assistência' },
    { valor: 'gestao', rotulo: 'Gestão' },
    { valor: 'pic', rotulo: 'Práticas Integrativas (PIC)' },
    { valor: 'outro', rotulo: 'Outra atividade' }
  ];

  groupForm = this.fb.group({
    code: ['', Validators.required],
    name: ['', Validators.required],
    description: ['']
  });

  /** Alocação do rodízio da turma, preenchida pelo supervisor. */
  scheduleForm = this.fb.group({
    groupId: ['', Validators.required],
    shift: ['manha', Validators.required],
    periodLabel: ['', Validators.required],
    locationId: ['', Validators.required],
    preceptorId: ['', Validators.required],
    startDate: ['', Validators.required],
    endDate: ['', Validators.required],
    activityType: ['assistencia', Validators.required],
    requiredHours: [80, [Validators.required, Validators.min(1), Validators.max(2000)]],
    notes: ['']
  });

  constructor(
    private groupsService: GroupsService,
    private locationsService: LocationsService,
    private usersService: UsersService,
    private snackBar: MatSnackBar,
    private dialog: MatDialog,
    private fb: FormBuilder
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
    this.loadAllSchedules();
    this.scheduleForm.valueChanges.subscribe(v => this.formValue.set(v as Record<string, unknown>));
    // Apenas preceptores: são eles que realizam o acompanhamento dos alunos alocados.
    this.usersService.getPreceptors().subscribe(p =>
      this.preceptors.set(p.filter(u => u.role === 'preceptor' && u.isActive !== false))
    );
    this.load();
  }

  load(): void {
    this.groupsService.getAll().subscribe({
      next: (g) => {
        this.groups.set(g);
        this.loading.set(false);
        g.forEach(grp => { this.loadSchedules(grp.id); this.loadMembers(grp.id); });
      },
      error: () => this.loading.set(false)
    });
  }

  /** Escalas de todas as turmas — base do aviso de conflito de preceptor. */
  loadAllSchedules(): void {
    this.groupsService.getSchedules().subscribe(s => this.allSchedules.set(s));
  }

  loadSchedules(groupId: string): void {
    this.groupsService.getSchedules(groupId).subscribe(s => {
      this.schedulesByGroup.update(cur => ({ ...cur, [groupId]: s }));
    });
  }

  /** Carrega os alunos vinculados para conferência antes da alocação. */
  loadMembers(groupId: string): void {
    this.groupsService.getMembers(groupId).subscribe(m => {
      this.membersByGroup.update(cur => ({ ...cur, [groupId]: m }));
    });
  }

  membros(groupId: string): GroupMember[] { return this.membersByGroup()[groupId] ?? []; }

  escalas(groupId: string): RotationSchedule[] { return this.schedulesByGroup()[groupId] ?? []; }

  /** A turma só pode receber rodízio depois que os alunos forem vinculados. */
  temAlunosVinculados(group: StudentGroup): boolean {
    const carregados = this.membersByGroup()[group.id];
    return carregados ? carregados.length > 0 : group.memberCount > 0;
  }

  // ── Turmas ────────────────────────────────────────────────────────────────
  saveGroup(): void {
    if (this.groupForm.invalid) return;
    this.groupsService.create(this.groupForm.value as any).subscribe({
      next: () => {
        this.snackBar.open('Turma criada!', '', { duration: 2000, panelClass: 'snack-success' });
        this.showGroupForm.set(false);
        this.groupForm.reset({ code: '', name: '', description: '' });
        this.load();
      },
      error: (err) => this.snackBar.open(err?.error?.message ?? 'Erro ao criar turma', '', { duration: 3000, panelClass: 'snack-error' })
    });
  }

  deleteGroup(id: string): void {
    if (!confirm('Excluir turma? As escalas de rodízio vinculadas também serão removidas.')) return;
    this.groupsService.delete(id).subscribe({
      next: () => this.load(),
      error: () => this.snackBar.open('Erro ao excluir', '', { duration: 3000, panelClass: 'snack-error' })
    });
  }

  // ── Vínculo dos alunos ────────────────────────────────────────────────────
  /**
   * Monta a turma. É o passo que faltava para liberar a alocação: sem vínculo,
   * o backend recusa o rodízio e o aluno não consegue fazer check-in.
   */
  vincularAlunos(group: StudentGroup): void {
    const ref = this.dialog.open(VincularAlunosDialogComponent, {
      width: '560px',
      data: { group }
    });

    ref.afterClosed().subscribe((res: VincularAlunosResult | null) => {
      if (!res) return;
      this.snackBar.open(
        `${res.vinculados} aluno(s) vinculado(s), ${res.desvinculados} desvinculado(s).`,
        '', { duration: 3000, panelClass: 'snack-success' });
      // Recarrega a turma para atualizar memberCount e liberar a alocação.
      this.loadMembers(group.id);
      this.load();
    });
  }

  // ── Alocação de rodízio ───────────────────────────────────────────────────
  abrirAlocacao(group: StudentGroup): void {
    if (!this.temAlunosVinculados(group)) {
      this.snackBar.open(
        `A turma ${group.code} não possui alunos vinculados. Use "Vincular alunos" antes de alocar o rodízio.`,
        'OK', { duration: 6000, panelClass: 'snack-error' });
      return;
    }
    this.editingSchedule.set(null);
    this.scheduleGroup.set(group);
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
    this.showScheduleForm.set(true);
  }

  editarAlocacao(group: StudentGroup, s: RotationSchedule): void {
    this.editingSchedule.set(s);
    this.scheduleGroup.set(group);
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
    this.showScheduleForm.set(true);
  }

  salvarAlocacao(): void {
    if (this.scheduleForm.invalid) {
      this.scheduleForm.markAllAsTouched();
      this.snackBar.open('Preencha todos os campos obrigatórios da alocação.', '', { duration: 3000, panelClass: 'snack-error' });
      return;
    }

    const v = this.scheduleForm.value;
    if (v.endDate! < v.startDate!) {
      this.snackBar.open('A data de término não pode ser anterior à data de início.', '', { duration: 4000, panelClass: 'snack-error' });
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
      notes: v.notes || undefined
    } as Partial<RotationSchedule>;

    const atual = this.editingSchedule();
    const op = atual
      ? this.groupsService.updateSchedule(atual.id, dto)
      : this.groupsService.createSchedule(dto);

    op.subscribe({
      next: () => {
        this.savingSchedule.set(false);
        this.snackBar.open(atual ? 'Alocação atualizada!' : 'Rodízio alocado!', '', { duration: 2500, panelClass: 'snack-success' });
        this.showScheduleForm.set(false);
        this.loadSchedules(v.groupId!);
        this.loadAllSchedules();
      },
      error: (err) => {
        this.savingSchedule.set(false);
        this.snackBar.open(err?.error?.message ?? 'Erro ao salvar alocação', 'OK', { duration: 6000, panelClass: 'snack-error' });
      }
    });
  }

  excluirAlocacao(groupId: string, s: RotationSchedule): void {
    if (!confirm(`Excluir o rodízio de ${s.periodLabel} em ${s.locationName}?`)) return;
    this.groupsService.deleteSchedule(s.id).subscribe({
      next: () => {
        this.snackBar.open('Alocação removida.', '', { duration: 2000 });
        this.loadSchedules(groupId);
        this.loadAllSchedules();
      },
      error: () => this.snackBar.open('Erro ao excluir alocação', '', { duration: 3000, panelClass: 'snack-error' })
    });
  }

  // ── Rótulos ───────────────────────────────────────────────────────────────
  turnoLabel(valor: string): string {
    return this.turnos.find(t => t.valor === valor)?.rotulo ?? valor;
  }

  atividadeLabel(valor: string): string {
    return this.atividades.find(a => a.valor === valor)?.rotulo ?? valor;
  }
}
