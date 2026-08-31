import { Component, OnDestroy, computed, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTableModule } from '@angular/material/table';
import { MatRadioModule } from '@angular/material/radio';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { Subscription, interval, switchMap } from 'rxjs';
import { UnidadesSaudeService } from '../../core/services/unidades-saude.service';
import { ImportPreview, ImportacaoProgresso, ImportacaoResultado } from '../../core/models/models';

/**
 * Importação de unidades por planilha.
 *
 * Fluxo: enviar → conferir a prévia → confirmar → acompanhar a geocodificação.
 * Nada é gravado antes da confirmação; a geocodificação roda em segundo plano,
 * no ritmo permitido pelo OpenStreetMap.
 */
@Component({
  selector: 'app-importar-unidades',
  standalone: true,
  imports: [
    CommonModule, FormsModule, RouterLink,
    MatCardModule, MatButtonModule, MatIconModule, MatTableModule, MatRadioModule,
    MatProgressBarModule, MatProgressSpinnerModule, MatTooltipModule, MatSnackBarModule
  ],
  templateUrl: './importar-unidades.component.html',
  styleUrls: ['./importar-unidades.component.scss']
})
export class ImportarUnidadesComponent implements OnDestroy {
  arquivo: File | null = null;
  acaoDuplicadas: 'ignorar' | 'atualizar' = 'ignorar';

  enviando = signal(false);
  confirmando = signal(false);
  previa = signal<ImportPreview | null>(null);
  resultado = signal<ImportacaoResultado | null>(null);
  progresso = signal<ImportacaoProgresso | null>(null);
  erro = signal('');

  colunas = ['linha', 'nome', 'endereco', 'status'];

  /** Linhas que serão de fato importadas com a escolha atual. */
  totalAImportar = computed(() => {
    const p = this.previa();
    if (!p) return 0;
    return this.acaoDuplicadas === 'atualizar' ? p.validas + p.duplicadas : p.validas;
  });

  private acompanhamento?: Subscription;

  constructor(
    private service: UnidadesSaudeService,
    private snackBar: MatSnackBar,
    private router: Router
  ) {}

  ngOnDestroy(): void { this.acompanhamento?.unsubscribe(); }

  urlModelo(): string { return this.service.urlModeloPlanilha(); }

  onArquivo(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.arquivo = input.files?.[0] ?? null;
    this.previa.set(null);
    this.resultado.set(null);
    this.erro.set('');
    if (this.arquivo) this.enviarParaPrevia();
  }

  enviarParaPrevia(): void {
    if (!this.arquivo) return;
    this.enviando.set(true);
    this.erro.set('');

    this.service.importarPreview(this.arquivo).subscribe({
      next: (p) => { this.enviando.set(false); this.previa.set(p); },
      error: (err) => {
        this.enviando.set(false);
        const corpo = err?.error;
        this.erro.set(corpo?.erros?.length ? corpo.erros.join(' ')
          : corpo?.message ?? 'Não foi possível ler a planilha.');
      }
    });
  }

  confirmar(): void {
    const p = this.previa();
    if (!p) return;

    this.confirmando.set(true);
    this.service.importarConfirmar(p.previewId, this.acaoDuplicadas).subscribe({
      next: (r) => {
        this.confirmando.set(false);
        this.resultado.set(r);
        this.previa.set(null);
        this.snackBar.open(r.mensagem, '', { duration: 6000, panelClass: 'snack-success' });
        if (r.enfileiradasParaGeocodificar > 0) this.acompanharProgresso(r.loteId);
      },
      error: (err) => {
        this.confirmando.set(false);
        this.erro.set(err?.error?.message ?? 'Erro ao confirmar a importação.');
      }
    });
  }

  /**
   * A geocodificação respeita ~1 requisição por segundo, então uma planilha grande
   * leva alguns minutos. Consultamos o progresso a cada 2s até concluir.
   */
  private acompanharProgresso(loteId: string): void {
    this.acompanhamento?.unsubscribe();
    this.acompanhamento = interval(2000)
      .pipe(switchMap(() => this.service.progressoImportacao(loteId)))
      .subscribe({
        next: (p) => {
          this.progresso.set(p);
          if (p.concluido) {
            this.acompanhamento?.unsubscribe();
            this.snackBar.open('Geocodificação concluída.', '',
              { duration: 5000, panelClass: 'snack-success' });
          }
        },
        error: () => this.acompanhamento?.unsubscribe()
      });
  }

  recomecar(): void {
    this.acompanhamento?.unsubscribe();
    this.arquivo = null;
    this.previa.set(null);
    this.resultado.set(null);
    this.progresso.set(null);
    this.erro.set('');
  }

  irParaRevisao(): void { this.router.navigate(['/app/unidades/revisao']); }

  rotuloStatusLinha(status: string): string {
    switch (status) {
      case 'valida': return 'Válida';
      case 'invalida': return 'Com erro';
      case 'duplicada': return 'Já cadastrada';
      case 'duplicada_endereco_alterado': return 'Já cadastrada (endereço mudou)';
      default: return status;
    }
  }

  classeStatusLinha(status: string): string {
    switch (status) {
      case 'valida': return 'linha-valida';
      case 'invalida': return 'linha-invalida';
      default: return 'linha-duplicada';
    }
  }
}
