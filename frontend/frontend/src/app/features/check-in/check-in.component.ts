import { Component, OnInit, OnDestroy, signal, computed, ViewChild, ElementRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatDividerModule } from '@angular/material/divider';
import { MatTooltipModule } from '@angular/material/tooltip';
import { AttendanceService } from '../../core/services/attendance.service';
import { ActiveSchedule } from '../../core/models/models';

@Component({
  selector: 'app-check-in',
  standalone: true,
  imports: [
    CommonModule, ReactiveFormsModule, RouterLink,
    MatCardModule, MatButtonModule, MatFormFieldModule, MatInputModule,
    MatIconModule, MatProgressSpinnerModule, MatSnackBarModule, MatDividerModule,
    MatTooltipModule
  ],
  templateUrl: './check-in.component.html',
  styleUrls: ['./check-in.component.scss']
})
export class CheckInComponent implements OnInit, OnDestroy {
  @ViewChild('video') videoRef?: ElementRef<HTMLVideoElement>;

  activeSchedule = signal<ActiveSchedule | null>(null);
  openCheckIn = signal<{ id: string; recorded_at: string } | null>(null);
  busy = signal(false);
  gettingLocation = signal(false);
  lat = signal<number | null>(null);
  lon = signal<number | null>(null);
  accuracy = signal<number | null>(null);
  photoBase64 = signal<string | null>(null);
  cameraOn = signal(false);

  private stream: MediaStream | null = null;

  /** Distância (m) entre a posição capturada e o local da escala ativa. */
  distance = computed<number | null>(() => {
    const sched = this.activeSchedule();
    const la = this.lat(), lo = this.lon();
    if (!sched || la === null || lo === null) return null;
    return this.haversine(la, lo, sched.location.latitude, sched.location.longitude);
  });

  /** Dentro do raio, considerando a precisão do GPS (mais tolerante). */
  inRadius = computed<boolean | null>(() => {
    const d = this.distance();
    const sched = this.activeSchedule();
    if (d === null || !sched) return null;
    const efetiva = Math.max(0, d - (this.accuracy() ?? 0));
    return efetiva <= sched.location.radiusMeters;
  });

  /**
   * O ponto só é liberado dentro do raio da unidade alocada. Enquanto isso não
   * for confirmado, a foto e a confirmação ficam bloqueadas — o backend recusa
   * o registro de qualquer forma, então liberar a câmera antes só faria o aluno
   * perder tempo com um ponto que não seria aceito.
   */
  podeRegistrar = computed(() => this.inRadius() === true);

  /** Quanto falta andar para entrar no raio da unidade. */
  metrosFaltando = computed<number | null>(() => {
    const d = this.distance();
    const sched = this.activeSchedule();
    if (d === null || !sched) return null;
    const efetiva = Math.max(0, d - (this.accuracy() ?? 0));
    return Math.max(0, Math.round(efetiva - sched.location.radiusMeters));
  });

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

  ngOnDestroy(): void {
    this.stopCamera();
  }

  loadState(): void {
    this.attendanceService.getActiveSchedule().subscribe({ next: (s) => this.activeSchedule.set(s), error: () => {} });
    this.attendanceService.getOpenCheckIn().subscribe({ next: (r) => this.openCheckIn.set(r), error: () => {} });
  }

  getLocation(): void {
    if (!navigator.geolocation) { this.snackBar.open('GPS não disponível', '', { duration: 3000 }); return; }
    this.gettingLocation.set(true);
    navigator.geolocation.getCurrentPosition(
      (pos) => {
        this.lat.set(pos.coords.latitude);
        this.lon.set(pos.coords.longitude);
        this.accuracy.set(pos.coords.accuracy ?? null);
        this.gettingLocation.set(false);

        // Se a nova posição ficou fora do raio, a foto anterior não vale mais.
        if (!this.podeRegistrar()) {
          this.stopCamera();
          this.photoBase64.set(null);
        }
      },
      () => { this.snackBar.open('Não foi possível obter localização', '', { duration: 3000 }); this.gettingLocation.set(false); },
      { enableHighAccuracy: true, timeout: 10000 }
    );
  }

  // ── Câmera ao vivo ─────────────────────────────────────────────────────────
  async startCamera(): Promise<void> {
    if (!this.podeRegistrar()) {
      this.snackBar.open(
        this.lat() === null
          ? 'Capture sua localização antes de tirar a foto.'
          : 'Você está fora do raio da unidade. Aproxime-se para registrar o ponto.',
        '', { duration: 4000, panelClass: 'snack-error' });
      return;
    }

    try {
      this.stream = await navigator.mediaDevices.getUserMedia({
        video: { facingMode: 'environment', width: { ideal: 1280 }, height: { ideal: 720 } },
        audio: false
      });
      this.cameraOn.set(true);
      // aguarda o <video> aparecer no DOM
      setTimeout(() => {
        const v = this.videoRef?.nativeElement;
        if (v && this.stream) { v.srcObject = this.stream; v.play().catch(() => {}); }
      }, 0);
    } catch {
      this.snackBar.open('Não foi possível acessar a câmera. Verifique as permissões.', '', { duration: 4000 });
    }
  }

  takePhoto(): void {
    const v = this.videoRef?.nativeElement;
    if (!v || !v.videoWidth) { this.snackBar.open('Câmera ainda carregando, tente novamente.', '', { duration: 3000 }); return; }
    // Redimensiona para no máximo 800px de largura para reduzir o tamanho.
    const maxW = 800;
    const scale = Math.min(1, maxW / v.videoWidth);
    const canvas = document.createElement('canvas');
    canvas.width = Math.round(v.videoWidth * scale);
    canvas.height = Math.round(v.videoHeight * scale);
    const ctx = canvas.getContext('2d');
    if (!ctx) return;
    ctx.drawImage(v, 0, 0, canvas.width, canvas.height);
    this.photoBase64.set(canvas.toDataURL('image/jpeg', 0.6));
    this.stopCamera();
  }

  retakePhoto(): void {
    this.photoBase64.set(null);
    this.startCamera();
  }

  private stopCamera(): void {
    this.stream?.getTracks().forEach(t => t.stop());
    this.stream = null;
    this.cameraOn.set(false);
  }

  /** Distância em metros entre duas coordenadas (fórmula de Haversine). */
  private haversine(lat1: number, lon1: number, lat2: number, lon2: number): number {
    const R = 6371000;
    const toRad = (g: number) => (g * Math.PI) / 180;
    const dLat = toRad(lat2 - lat1);
    const dLon = toRad(lon2 - lon1);
    const a = Math.sin(dLat / 2) ** 2 +
      Math.cos(toRad(lat1)) * Math.cos(toRad(lat2)) * Math.sin(dLon / 2) ** 2;
    return R * 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a));
  }

  register(type: 'check_in' | 'check_out'): void {
    if (this.lat() === null || this.lon() === null) { this.snackBar.open('Capture sua localização primeiro', '', { duration: 3000 }); return; }
    if (!this.activeSchedule()) { this.snackBar.open('Nenhuma escala ativa no momento', '', { duration: 3000 }); return; }
    if (!this.podeRegistrar()) {
      this.snackBar.open(
        `Você está fora do raio de ${this.activeSchedule()!.location.name}. ` +
        'Aproxime-se da unidade ou registre uma irregularidade.',
        '', { duration: 6000, panelClass: 'snack-error' });
      return;
    }
    if (!this.photoBase64()) { this.snackBar.open('Tire a foto do registro primeiro', '', { duration: 3000 }); return; }
    this.busy.set(true);
    this.attendanceService.create({
      scheduleId: this.activeSchedule()!.scheduleId,
      locationId: this.activeSchedule()!.location.id,
      type,
      latitude: this.lat()!,
      longitude: this.lon()!,
      accuracyMeters: this.accuracy() ?? undefined,
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
