import { Component, OnInit, signal } from '@angular/core';
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
import { GroupsService } from '../../core/services/groups.service';
import { LocationsService } from '../../core/services/locations.service';
import { StudentGroup, RotationSchedule, Location } from '../../core/models/models';

@Component({
  selector: 'app-rodizios',
  standalone: true,
  imports: [
    CommonModule, ReactiveFormsModule,
    MatCardModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule,
    MatSelectModule, MatTableModule, MatExpansionModule, MatProgressSpinnerModule, MatSnackBarModule
  ],
  templateUrl: './rodizios.component.html',
  styleUrls: ['./rodizios.component.scss']
})
export class RodiziosComponent implements OnInit {
  groups = signal<StudentGroup[]>([]);
  locations = signal<Location[]>([]);
  schedulesByGroup = signal<Record<string, RotationSchedule[]>>({});
  loading = signal(true);
  showGroupForm = signal(false);

  groupForm = this.fb.group({
    code: ['', Validators.required],
    name: ['', Validators.required],
    description: ['']
  });

  constructor(private groupsService: GroupsService, private locationsService: LocationsService, private snackBar: MatSnackBar, private fb: FormBuilder) {}

  ngOnInit(): void {
    this.locationsService.getAll().subscribe(l => this.locations.set(l));
    this.load();
  }

  load(): void {
    this.groupsService.getAll().subscribe({
      next: (g) => { this.groups.set(g); this.loading.set(false); g.forEach(grp => this.loadSchedules(grp.id)); },
      error: () => this.loading.set(false)
    });
  }

  loadSchedules(groupId: string): void {
    this.groupsService.getSchedules(groupId).subscribe(s => {
      this.schedulesByGroup.update(cur => ({ ...cur, [groupId]: s }));
    });
  }

  saveGroup(): void {
    if (this.groupForm.invalid) return;
    this.groupsService.create(this.groupForm.value as any).subscribe({
      next: () => { this.snackBar.open('Turma criada!', '', { duration: 2000 }); this.showGroupForm.set(false); this.load(); },
      error: () => this.snackBar.open('Erro ao criar turma', '', { duration: 3000 })
    });
  }

  deleteGroup(id: string): void {
    if (!confirm('Excluir turma?')) return;
    this.groupsService.delete(id).subscribe({ next: () => this.load(), error: () => this.snackBar.open('Erro ao excluir', '', { duration: 3000 }) });
  }
}
