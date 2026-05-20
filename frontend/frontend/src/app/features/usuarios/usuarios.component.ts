import { Component, OnInit, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormsModule, FormBuilder, Validators } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTableModule } from '@angular/material/table';
import { MatSelectModule } from '@angular/material/select';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatDialogModule, MatDialog } from '@angular/material/dialog';
import { MatTabsModule } from '@angular/material/tabs';
import { MatChipsModule } from '@angular/material/chips';
import { MatDividerModule } from '@angular/material/divider';
import { MatTooltipModule } from '@angular/material/tooltip';
import { UsersService } from '../../core/services/users.service';
import { GroupsService } from '../../core/services/groups.service';
import { UserDto, StudentGroup, BulkImportStudent, BulkImportResult } from '../../core/models/models';
import * as XLSX from 'xlsx';
import { ImportarAlunosDialogComponent } from './importar-alunos-dialog.component';
import { CadastrarStaffDialogComponent } from './cadastrar-staff-dialog.component';

type Shift = 'manha' | 'tarde' | 'noite';
type Sem   = 7 | 8;

interface TabKey { sem: Sem; shift: Shift; label: string }

@Component({
  selector: 'app-usuarios',
  standalone: true,
  imports: [
    CommonModule, FormsModule, ReactiveFormsModule,
    MatCardModule, MatButtonModule, MatIconModule, MatTableModule,
    MatSelectModule, MatFormFieldModule, MatInputModule,
    MatProgressSpinnerModule, MatSnackBarModule, MatDialogModule,
    MatTabsModule, MatChipsModule, MatDividerModule, MatTooltipModule
  ],
  templateUrl: './usuarios.component.html',
  styleUrls: ['./usuarios.component.scss']
})
export class UsuariosComponent implements OnInit {
  users    = signal<UserDto[]>([]);
  groups   = signal<StudentGroup[]>([]);
  loading  = signal(true);
  advancingBusy = signal(false);

  readonly tabs: TabKey[] = [
    { sem: 7, shift: 'manha',  label: '7° Manhã'   },
    { sem: 7, shift: 'tarde',  label: '7° Tarde'   },
    { sem: 7, shift: 'noite',  label: '7° Noite'   },
    { sem: 8, shift: 'manha',  label: '8° Manhã'   },
    { sem: 8, shift: 'tarde',  label: '8° Tarde'   },
    { sem: 8, shift: 'noite',  label: '8° Noite'   },
  ];

  activeStudents = computed(() =>
    this.users().filter(u => u.role === 'aluno' && u.isActive !== false && (u.semester === 7 || u.semester === 8))
  );

  uncategorizedStudents = computed(() =>
    this.users().filter(u => u.role === 'aluno' && u.isActive !== false && !u.semester)
  );

  inactiveStudents = computed(() =>
    this.users().filter(u => u.role === 'aluno' && u.isActive === false)
  );

  staff = computed(() =>
    this.users().filter(u => u.role !== 'aluno')
  );

  displayedStaffCols = ['name', 'email', 'role'];

  constructor(
    private usersService: UsersService,
    private groupsService: GroupsService,
    private snackBar: MatSnackBar,
    private dialog: MatDialog
  ) {}

  ngOnInit(): void {
    this.groupsService.getAll().subscribe(g => this.groups.set(g));
    this.loadUsers();
  }

  loadUsers(): void {
    this.loading.set(true);
    this.usersService.getAll().subscribe({
      next: (u) => { this.users.set(u); this.loading.set(false); },
      error: () => this.loading.set(false)
    });
  }

  studentsForTab(tab: TabKey): UserDto[] {
    return this.activeStudents().filter(u => u.semester === tab.sem && u.shift === tab.shift);
  }

  countForTab(tab: TabKey): number {
    return this.studentsForTab(tab).length;
  }

  openImportDialog(): void {
    const ref = this.dialog.open(ImportarAlunosDialogComponent, { width: '520px', disableClose: false });
    ref.afterClosed().subscribe((result: BulkImportResult | null) => {
      if (result) {
        this.snackBar.open(
          `${result.imported} aluno(s) importado(s), ${result.updated} atualizado(s).` +
          (result.errors.length ? ` ${result.errors.length} erro(s).` : ''),
          'OK',
          { duration: 5000, panelClass: result.errors.length ? 'snack-error' : 'snack-success' }
        );
        this.loadUsers();
      }
    });
  }

  openCadastrarStaffDialog(): void {
    const ref = this.dialog.open(CadastrarStaffDialogComponent, { width: '480px', disableClose: false });
    ref.afterClosed().subscribe((created: boolean) => {
      if (created) { this.snackBar.open('Usuário cadastrado!', '', { duration: 2500, panelClass: 'snack-success' }); this.loadUsers(); }
    });
  }

  onAdvanceSemester(): void {
    if (!confirm('Confirma o avanço de semestre? Todos os alunos do 7° semestre passarão para o 8°, e alunos do 8° serão marcados como formados.')) return;
    this.advancingBusy.set(true);
    this.usersService.advanceSemester().subscribe({
      next: (res) => {
        this.advancingBusy.set(false);
        this.snackBar.open(`${res.advanced} aluno(s) avançaram. ${res.graduated} formado(s).`, 'OK', { duration: 6000, panelClass: 'snack-success' });
        this.loadUsers();
      },
      error: () => { this.advancingBusy.set(false); this.snackBar.open('Erro ao avançar semestre', '', { duration: 4000, panelClass: 'snack-error' }); }
    });
  }

  groupName(groupId: string | null | undefined): string {
    if (!groupId) return '—';
    return this.groups().find(g => g.id === groupId)?.name ?? '—';
  }
}

