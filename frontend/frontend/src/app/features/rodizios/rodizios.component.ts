import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import { MatPaginatorModule } from '@angular/material/paginator';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatTableModule } from '@angular/material/table';
import { MatSortModule } from '@angular/material/sort';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { GroupsService } from '../../core/services/groups.service';
import { StudentGroup } from '../../core/models/models';
import { Paginacao, normalizarBusca } from '../../core/utils/paginacao';
import { Ordenacao } from '../../core/utils/ordenacao';
import { CAMPOS_TIPADOS } from '../../core/utils/campos.directive';
import { mensagemErro } from '../../core/utils/api-error';

/**
 * Lista das turmas. O gerenciamento de cada uma (alunos, rodízios e alocação)
 * fica na página da turma — antes era um painel expansível aqui dentro, que no
 * celular sobrepunha nome, código e contagem de alunos.
 */
@Component({
  selector: 'app-rodizios',
  standalone: true,
  imports: [
    CommonModule, ReactiveFormsModule, MatPaginatorModule,
    MatCardModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule,
    MatTableModule, MatSortModule, MatProgressSpinnerModule, MatSnackBarModule,
    ...CAMPOS_TIPADOS
  ],
  templateUrl: './rodizios.component.html',
  styleUrls: ['./rodizios.component.scss']
})
export class RodiziosComponent implements OnInit {
  private readonly router = inject(Router);

  groups = signal<StudentGroup[]>([]);
  /** Quantidade de rodízios por turma, para a coluna da lista. */
  rodiziosPorTurma = signal<Record<string, number>>({});
  loading = signal(true);
  showGroupForm = signal(false);

  readonly buscaTurma = signal('');

  readonly turmasFiltradas = computed(() => {
    const termo = normalizarBusca(this.buscaTurma());
    return termo
      ? this.groups().filter(g => [g.code, g.name].some(c => normalizarBusca(c).includes(termo)))
      : this.groups();
  });
  readonly ordenacao = new Ordenacao(this.turmasFiltradas, {
    rodizios: g => this.qtdRodizios(g.id)
  }, { active: 'code', direction: 'asc' });
  readonly paginacao = new Paginacao(this.ordenacao.itens);

  readonly colunas = ['code', 'name', 'memberCount', 'rodizios', 'abrir'];

  groupForm = this.fb.group({
    code: ['', [Validators.required, Validators.maxLength(20)]],
    name: ['', [Validators.required, Validators.maxLength(200)]],
    description: ['']
  });

  constructor(
    private groupsService: GroupsService,
    private snackBar: MatSnackBar,
    private fb: FormBuilder
  ) {}

  ngOnInit(): void {
    this.load();
    this.groupsService.getSchedules().subscribe(s => {
      const porTurma: Record<string, number> = {};
      for (const escala of s) porTurma[escala.groupId] = (porTurma[escala.groupId] ?? 0) + 1;
      this.rodiziosPorTurma.set(porTurma);
    });
  }

  load(): void {
    this.groupsService.getAll().subscribe({
      next: (g) => {
        this.groups.set(g);
        this.loading.set(false);
      },
      error: () => this.loading.set(false)
    });
  }

  qtdRodizios(groupId: string): number {
    return this.rodiziosPorTurma()[groupId] ?? 0;
  }

  abrir(g: StudentGroup): void {
    this.router.navigate(['/app/rodizios', g.id]);
  }

  saveGroup(): void {
    if (this.groupForm.invalid) return;
    this.groupsService.create(this.groupForm.value as any).subscribe({
      next: (criada) => {
        this.snackBar.open('Turma criada!', '', { duration: 2000, panelClass: 'snack-success' });
        this.showGroupForm.set(false);
        this.groupForm.reset({ code: '', name: '', description: '' });
        // A turma nova já abre pronta para vincular os alunos.
        if (criada?.id) this.abrir(criada);
        else this.load();
      },
      error: (err) => this.snackBar.open(mensagemErro(err, 'Erro ao criar turma'), '', { duration: 3000, panelClass: 'snack-error' })
    });
  }
}
