import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatTableModule } from '@angular/material/table';
import { MatDialogModule, MatDialog } from '@angular/material/dialog';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { LocationsService } from '../../core/services/locations.service';
import { Location } from '../../core/models/models';

@Component({
  selector: 'app-locais',
  standalone: true,
  imports: [
    CommonModule, ReactiveFormsModule,
    MatCardModule, MatButtonModule, MatIconModule, MatFormFieldModule,
    MatInputModule, MatTableModule, MatProgressSpinnerModule, MatSnackBarModule, MatDialogModule
  ],
  templateUrl: './locais.component.html',
  styleUrls: ['./locais.component.scss']
})
export class LocaisComponent implements OnInit {
  locations = signal<Location[]>([]);
  loading = signal(true);
  showForm = signal(false);
  editing = signal<Location | null>(null);
  displayedColumns = ['name', 'address', 'radius', 'shift', 'actions'];

  form = this.fb.group({
    name: ['', Validators.required],
    address: [''],
    latitude: [0, Validators.required],
    longitude: [0, Validators.required],
    radiusMeters: [100, [Validators.required, Validators.min(10)]],
    isInstitution: [true],
    shiftStart: ['07:00'],
    shiftEnd: ['13:00']
  });

  constructor(private locationsService: LocationsService, private snackBar: MatSnackBar, private fb: FormBuilder) {}

  ngOnInit(): void { this.load(); }

  load(): void {
    this.locationsService.getAll().subscribe({ next: (l) => { this.locations.set(l); this.loading.set(false); }, error: () => this.loading.set(false) });
  }

  openCreate(): void { this.editing.set(null); this.form.reset({ radiusMeters: 100, isInstitution: true, shiftStart: '07:00', shiftEnd: '13:00', latitude: 0, longitude: 0 }); this.showForm.set(true); }

  openEdit(loc: Location): void {
    this.editing.set(loc);
    this.form.patchValue({ name: loc.name, address: loc.address, latitude: loc.latitude, longitude: loc.longitude, radiusMeters: loc.radiusMeters, isInstitution: loc.isInstitution, shiftStart: loc.shiftStart, shiftEnd: loc.shiftEnd });
    this.showForm.set(true);
  }

  save(): void {
    if (this.form.invalid) return;
    const dto = this.form.value as any;
    const op = this.editing()
      ? this.locationsService.update(this.editing()!.id, dto)
      : this.locationsService.create(dto);
    op.subscribe({
      next: () => { this.snackBar.open('Salvo!', '', { duration: 2000 }); this.showForm.set(false); this.load(); },
      error: () => this.snackBar.open('Erro ao salvar', '', { duration: 3000 })
    });
  }

  delete(id: string): void {
    if (!confirm('Excluir este local?')) return;
    this.locationsService.delete(id).subscribe({ next: () => { this.snackBar.open('Excluído', '', { duration: 2000 }); this.load(); }, error: () => this.snackBar.open('Erro ao excluir', '', { duration: 3000 }) });
  }
}
