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
import { MatChipsModule } from '@angular/material/chips';
import { AttendanceService } from '../../core/services/attendance.service';
import { ProgramacaoService } from '../../core/services/programacao.service';
import { AtividadesRemotasService } from '../../core/services/atividades-remotas.service';
import { AtividadeRemotaAluno, ProgramacaoDia, ShiftPointStatus } from '../../core/models/models';

/**
 * Registro de presença guiado pela programação do dia.
 *
 * A tela não pergunta mais "onde você está?", e sim "o que estava programado
 * para hoje?". A resposta decide o que ela pede: em dia presencial, localização
 * e foto; em dia remoto, o código da atividade e a tarefa; em feriado, recesso
 * ou fim de semana, nada — não há ponto a registrar.
 */
@Component({
  selector: 'app-check-in',
  standalone: true,
  imports: [
    CommonModule, ReactiveFormsModule, RouterLink,
    MatCardModule, MatButtonModule, MatFormFieldModule, MatInputModule,
    MatIconModule, MatProgressSpinnerModule, MatSnackBarModule, MatDividerModule,
    MatTooltipModule, MatChipsModule
  ],
  templateUrl: './check-in.component.html',
  styleUrls: ['./check-in.component.scss']
})
export class CheckInComponent implements OnInit, OnDestroy {
  @ViewChild('video') videoRef?: ElementRef<HTMLVideoElement>;

  /** Programação de hoje: é ela que define o que a tela pede. */
  programacao = signal<ProgramacaoDia | null>(null);
  /** Situação do ponto no turno corrente: 1 check-in e 1 check-out, no máximo. */
  shiftStatus = signal<ShiftPointStatus | null>(null);
  loading = signal(true);
  busy = signal(false);
  gettingLocation = signal(false);
  lat = signal<number | null>(null);
  lon = signal<number | null>(null);
  accuracy = signal<number | null>(null);
  photoBase64 = signal<string | null>(null);
  cameraOn = signal(false);

  private stream: MediaStream | null = null;

  // ── Modo do dia ───────────────────────────────────────────────────────────
  presencial = computed(() => this.programacao()?.modo === 'presencial');
  remoto = computed(() => this.programacao()?.modo === 'remoto');
  semAtividade = computed(() => this.programacao()?.modo === 'sem_atividade');

  /** Unidade em que o ponto de hoje deve ser registrado. */
  local = computed(() => this.programacao()?.location ?? null);

  /** Atividades remotas de hoje ainda em aberto para o aluno. */
  atividadesDisponiveis = computed<AtividadeRemotaAluno[]>(() =>
    (this.programacao()?.atividadesRemotas ?? []).filter(a => !a.jaRegistrada));

  /** Atividades de hoje em que o aluno já registrou participação. */
  atividadesRegistradas = computed<AtividadeRemotaAluno[]>(() =>
    (this.programacao()?.atividadesRemotas ?? []).filter(a => a.jaRegistrada));

  /**
   * Alguma atividade de hoje exige entrega além do código. Enquanto houver, o
   * campo de resposta fica visível: o aluno não sabe de antemão a qual código
   * vai responder.
   */
  exigeTarefa = computed(() => this.atividadesDisponiveis().some(a => a.requiresTask));

  /** Distância (m) entre a posição capturada e a unidade programada. */
  distance = computed<number | null>(() => {
    const local = this.local();
    const la = this.lat(), lo = this.lon();
    if (!local || la === null || lo === null) return null;
    return this.haversine(la, lo, local.latitude, local.longitude);
  });

  /** Dentro do raio, considerando a precisão do GPS (mais tolerante). */
  inRadius = computed<boolean | null>(() => {
    const d = this.distance();
    const local = this.local();
    if (d === null || !local) return null;
    return Math.max(0, d - (this.accuracy() ?? 0)) <= local.radiusMeters;
  });

  /**
   * O ponto só é liberado dentro do raio da unidade programada. Enquanto isso
   * não for confirmado, a foto e a confirmação ficam bloqueadas — o backend
   * recusa o registro de qualquer forma, então liberar a câmera antes só faria
   * o aluno perder tempo com um ponto que não seria aceito.
   */
  podeRegistrar = computed(() => this.inRadius() === true);

  /**
   * Espelha a validade do campo de descrição em um sinal: `descricaoPendente` é
   * um computed e só reage a sinais, não a um FormControl.
   */
  private descricaoPreenchida = signal(false);

  /** Ação que a tela vai registrar agora — nada, se o turno já estiver fechado. */
  proximaAcao = computed<'check_in' | 'check_out' | null>(() => {
    const s = this.shiftStatus();
    if (!s) return null;
    if (s.canCheckIn) return 'check_in';
    if (s.canCheckOut) return 'check_out';
    return null;
  });

  /** O turno já tem entrada e saída: não há mais o que registrar hoje nele. */
  turnoFechado = computed(() => this.shiftStatus()?.shiftClosed === true);

  /**
   * A descrição das atividades é obrigatória no check-out — a API recusa o
   * fechamento sem ela, então o botão só libera com o campo preenchido.
   */
  descricaoPendente = computed(() =>
    this.proximaAcao() === 'check_out' && !this.descricaoPreenchida());

  /** Quanto falta andar para entrar no raio da unidade. */
  metrosFaltando = computed<number | null>(() => {
    const d = this.distance();
    const local = this.local();
    if (d === null || !local) return null;
    return Math.max(0, Math.round(Math.max(0, d - (this.accuracy() ?? 0)) - local.radiusMeters));
  });

  descForm = this.fb.group({
    activitiesDescription: ['', [Validators.required, Validators.minLength(10)]]
  });

  /** Registro da presença remota: o código e, quando exigida, a tarefa. */
  remotoForm = this.fb.group({
    code: ['', [Validators.required, Validators.minLength(4)]],
    taskResponse: ['']
  });

  constructor(
    private attendanceService: AttendanceService,
    private programacaoService: ProgramacaoService,
    private atividadesService: AtividadesRemotasService,
    private snackBar: MatSnackBar,
    private fb: FormBuilder
  ) {}

  ngOnInit(): void {
    this.descForm.controls['activitiesDescription'].valueChanges.subscribe(() =>
      this.descricaoPreenchida.set(this.descForm.controls['activitiesDescription'].valid));
    this.loadState();
  }

  ngOnDestroy(): void {
    this.stopCamera();
  }

  loadState(): void {
    this.loading.set(true);
    this.programacaoService.getDia().subscribe({
      next: (p) => { this.programacao.set(p); this.loading.set(false); },
      error: () => this.loading.set(false)
    });
    this.attendanceService.getShiftStatus().subscribe({
      next: (s) => this.shiftStatus.set(s), error: () => {}
    });
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

  // ── Ponto presencial ───────────────────────────────────────────────────────
  register(type: 'check_in' | 'check_out' | null): void {
    if (!type) {
      this.snackBar.open(this.shiftStatus()?.blockedReason ?? 'Nada a registrar neste turno.', '',
        { duration: 5000, panelClass: 'snack-error' });
      return;
    }
    if (this.lat() === null || this.lon() === null) { this.snackBar.open('Capture sua localização primeiro', '', { duration: 3000 }); return; }
    const local = this.local();
    if (!local) { this.snackBar.open('Nenhuma unidade programada para hoje', '', { duration: 3000 }); return; }
    if (!this.podeRegistrar()) {
      this.snackBar.open(
        `Você está fora do raio de ${local.name}. ` +
        'Aproxime-se da unidade ou registre uma irregularidade.',
        '', { duration: 6000, panelClass: 'snack-error' });
      return;
    }
    if (!this.photoBase64()) { this.snackBar.open('Tire a foto do registro primeiro', '', { duration: 3000 }); return; }
    // Sem descrição o check-out não é aceito: a API recusa e o registro se perderia.
    if (type === 'check_out' && this.descForm.invalid) {
      this.descForm.markAllAsTouched();
      this.snackBar.open('Descreva as atividades realizadas no turno para finalizar o check-out.', '',
        { duration: 5000, panelClass: 'snack-error' });
      return;
    }
    this.busy.set(true);
    this.attendanceService.create({
      scheduleId: this.programacao()?.scheduleId,
      locationId: local.id,
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
        this.descForm.reset({ activitiesDescription: '' });
        this.photoBase64.set(null);
        this.loadState();
      },
      error: (err) => {
        this.snackBar.open(err?.error?.message ?? 'Erro ao registrar', '', { duration: 5000, panelClass: 'snack-error' });
        this.busy.set(false);
      }
    });
  }

  // ── Presença remota ────────────────────────────────────────────────────────
  /**
   * Registra a participação pelo código. As recusas vêm da API com o motivo
   * (código errado, prazo encerrado, grupo não autorizado) e são exibidas como
   * chegam: é o que diz ao aluno o que fazer em seguida.
   */
  registrarRemoto(): void {
    if (this.remotoForm.invalid) {
      this.remotoForm.markAllAsTouched();
      this.snackBar.open('Informe o código de presença da atividade.', '',
        { duration: 4000, panelClass: 'snack-error' });
      return;
    }

    this.busy.set(true);
    const { code, taskResponse } = this.remotoForm.value;

    this.atividadesService.registrarPresenca(code!, taskResponse || undefined).subscribe({
      next: (res) => {
        this.busy.set(false);
        this.remotoForm.reset({ code: '', taskResponse: '' });
        this.snackBar.open(res.message, '', { duration: 5000, panelClass: 'snack-success' });
        this.loadState();
      },
      error: (err) => {
        this.busy.set(false);
        this.snackBar.open(err?.error?.message ?? 'Não foi possível registrar a participação.', 'OK',
          { duration: 7000, panelClass: 'snack-error' });
      }
    });
  }
}
