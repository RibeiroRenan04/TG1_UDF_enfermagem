import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { UnidadesSaudeService } from '../../core/services/unidades-saude.service';
import { UnidadeSaude } from '../../core/models/models';
import { STATUS_GEO } from './status-geocodificacao';

/**
 * Conferência das unidades cuja localização não ficou confirmada.
 *
 * Enquanto uma unidade não tem coordenada confiável, o check-in do aluno não pode
 * ser validado nela — por isso esta tela existe separada da listagem.
 */
@Component({
  selector: 'app-revisao-localizacao',
  standalone: true,
  imports: [
    CommonModule, FormsModule, RouterLink,
    MatCardModule, MatButtonModule, MatIconModule, MatFormFieldModule,
    MatInputModule, MatTooltipModule, MatProgressSpinnerModule, MatSnackBarModule
  ],
  templateUrl: './revisao-localizacao.component.html',
  styleUrls: ['./revisao-localizacao.component.scss']
})
export class RevisaoLocalizacaoComponent implements OnInit {
  unidades = signal<UnidadeSaude[]>([]);
  loading = signal(true);
  ocupadaId = signal<string | null>(null);

  /** Coordenadas em edição, por unidade. */
  edicao: Record<string, { lat: number | null; lon: number | null }> = {};

  readonly statusGeo = STATUS_GEO;

  constructor(private service: UnidadesSaudeService, private snackBar: MatSnackBar) {}

  ngOnInit(): void { this.carregar(); }

  carregar(): void {
    this.loading.set(true);
    this.service.getPendentesRevisao().subscribe({
      next: (u) => {
        this.unidades.set(u);
        this.edicao = {};
        for (const un of u)
          this.edicao[un.id] = {
            lat: un.temCoordenadas ? un.latitude : null,
            lon: un.temCoordenadas ? un.longitude : null
          };
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.snackBar.open('Erro ao carregar as unidades', '', { duration: 4000 });
      }
    });
  }

  /** Aprova a localização sugerida como está — passa a valer como conferida. */
  aprovar(u: UnidadeSaude): void {
    if (!u.temCoordenadas) return;
    this.salvarCoordenadas(u, u.latitude, u.longitude, 'Localização aprovada.');
  }

  /** Grava as coordenadas digitadas à mão. */
  salvarEdicao(u: UnidadeSaude): void {
    const e = this.edicao[u.id];
    if (e?.lat == null || e?.lon == null) {
      this.snackBar.open('Informe latitude e longitude.', '', { duration: 3000 });
      return;
    }
    if (e.lat < -90 || e.lat > 90 || e.lon < -180 || e.lon > 180) {
      this.snackBar.open('Coordenadas fora do intervalo válido.', '', { duration: 4000, panelClass: 'snack-error' });
      return;
    }
    this.salvarCoordenadas(u, e.lat, e.lon, 'Coordenadas atualizadas.');
  }

  private salvarCoordenadas(u: UnidadeSaude, lat: number, lon: number, mensagem: string): void {
    this.ocupadaId.set(u.id);
    this.service.definirCoordenadas(u.id, lat, lon).subscribe({
      next: () => {
        this.ocupadaId.set(null);
        this.snackBar.open(mensagem, '', { duration: 3000, panelClass: 'snack-success' });
        this.carregar();
      },
      error: (err) => {
        this.ocupadaId.set(null);
        this.snackBar.open(err?.error?.message ?? 'Erro ao salvar', '',
          { duration: 4000, panelClass: 'snack-error' });
      }
    });
  }

  geocodificar(u: UnidadeSaude): void {
    this.ocupadaId.set(u.id);
    this.service.geocodificar(u.id).subscribe({
      next: (r) => {
        this.ocupadaId.set(null);
        this.snackBar.open(
          r.sucesso ? `Localização encontrada (${r.precisao ?? 'sem detalhe'}).`
                    : r.mensagem ?? 'Não foi possível localizar.',
          '', { duration: 5000, panelClass: r.sucesso ? 'snack-success' : 'snack-error' });
        this.carregar();
      },
      error: (err) => {
        this.ocupadaId.set(null);
        this.snackBar.open(err?.error?.message ?? 'Erro ao geocodificar', '',
          { duration: 4000, panelClass: 'snack-error' });
      }
    });
  }

  urlMapa(lat: number, lon: number): string {
    return `https://www.openstreetmap.org/?mlat=${lat}&mlon=${lon}#map=17/${lat}/${lon}`;
  }

  rotuloStatus(s?: string): string { return this.statusGeo[s ?? 'pendente']?.rotulo ?? '—'; }
  classeStatus(s?: string): string { return this.statusGeo[s ?? 'pendente']?.classe ?? ''; }
  iconeStatus(s?: string): string { return this.statusGeo[s ?? 'pendente']?.icone ?? 'help'; }
}
