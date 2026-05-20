import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatDividerModule } from '@angular/material/divider';
import { AttendanceService } from '../../core/services/attendance.service';
import { ActiveSchedule } from '../../core/models/models';

@Component({
  selector: 'app-check-in',
  standalone: true,
  imports: [
    CommonModule, ReactiveFormsModule,
    MatCardModule, MatButtonModule, MatFormFieldModule, MatInputModule,
    MatIconModule, MatProgressSpinnerModule, MatSnackBarModule, MatDividerModule
  ],
  templateUrl: './check-in.component.html',
  styleUrls: ['./check-in.component.scss']
})
export class CheckInComponent implements OnInit {
  activeSchedule = signal<ActiveSchedule | null>(null);
  openCheckIn = signal<{ id: string; recorded_at: string } | null>(null);
  busy = signal(false);
  gettingLocation = signal(false);
  lat = signal<number | null>(null);
  lon = signal<number | null>(null);
  photoBase64 = signal<string | null>(null);

  descForm = this.fb.group({
    activitiesDescription: ['', Validators.required]
  });

  constructor(
    private attendanceService: AttendanceService,
    private snackBar: MatSnackBar,
    private fb: FormBuilder
  ) {}

  ngOnInit(): void {
    this.loadState();
  }

  loadState(): void {
    this.attendanceService.getActiveSchedule().subscribe({ next: (s) => this.activeSchedule.set(s), error: () => {} });
    this.attendanceService.getOpenCheckIn().subscribe({ next: (r) => this.openCheckIn.set(r), error: () => {} });
  }

  getLocation(): void {
    if (!navigator.geolocation) { this.snackBar.open('GPS não disponível', '', { duration: 3000 }); return; }
    this.gettingLocation.set(true);
    navigator.geolocation.getCurrentPosition(
      (pos) => { this.lat.set(pos.coords.latitude); this.lon.set(pos.coords.longitude); this.gettingLocation.set(false); },
      () => { this.snackBar.open('Não foi possível obter localização', '', { duration: 3000 }); this.gettingLocation.set(false); }
    );
  }

  onPhotoCapture(event: Event): void {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (!file) return;
    const reader = new FileReader();
    reader.onload = () => this.photoBase64.set(reader.result as string);
    reader.readAsDataURL(file);
  }

  register(type: 'check_in' | 'check_out'): void {
    if (!this.lat() || !this.lon()) { this.snackBar.open('Capture sua localização primeiro', '', { duration: 3000 }); return; }
    if (!this.activeSchedule()) { this.snackBar.open('Nenhum escala ativa no momento', '', { duration: 3000 }); return; }
    this.busy.set(true);
    this.attendanceService.create({
      scheduleId: this.activeSchedule()!.scheduleId,
      locationId: this.activeSchedule()!.location.id,
      type,
      latitude: this.lat()!,
      longitude: this.lon()!,
      photoBase64: this.photoBase64() ?? undefined,
      activitiesDescription: this.descForm.value.activitiesDescription ?? undefined
    }).subscribe({
      next: () => {
        this.snackBar.open(type === 'check_in' ? 'Check-in realizado!' : 'Check-out realizado!', '', { duration: 3000, panelClass: 'snack-success' });
        this.busy.set(false);
        this.loadState();
      },
      error: (err) => {
        this.snackBar.open(err?.error?.message ?? 'Erro ao registrar', '', { duration: 5000, panelClass: 'snack-error' });
        this.busy.set(false);
      }
    });
  }
}
