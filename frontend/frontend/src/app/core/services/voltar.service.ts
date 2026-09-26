import { Injectable, NgZone, inject } from '@angular/core';
import { MatDialog } from '@angular/material/dialog';

interface Camada {
  id: number;
  fechar: () => void;
}

/** Libera a camada que fechou por outro caminho (botão, backdrop, Esc). */
export type LiberarCamada = (opcoes?: { semVoltar?: boolean }) => void;

/**
 * Botão "voltar" do celular.
 *
 * Menu lateral, diálogos e visualizações que se abrem por cima da tela não
 * mudam a URL. Sem este serviço, o voltar do Android pulava direto para a
 * página anterior, deixando para trás a tela em que o usuário estava.
 *
 * Ao abrir uma camada, entra uma entrada extra no histórico com a mesma URL;
 * o voltar consome essa entrada e fecha só a camada do topo. Quando a camada
 * fecha por outro caminho, a entrada extra é retirada para o voltar seguinte
 * não "falhar" uma vez.
 */
@Injectable({ providedIn: 'root' })
export class VoltarService {
  private readonly zone = inject(NgZone);
  private readonly pilha: Camada[] = [];
  private sequencia = 0;
  /** Voltas disparadas por nós mesmos, que não devem fechar nada. */
  private ignorar = 0;
  private dialogosIniciados = false;

  constructor() {
    window.addEventListener('popstate', () => this.aoVoltar());
  }

  /** Registra uma camada aberta; o voltar do aparelho passa a fechá-la. */
  abrir(fechar: () => void): LiberarCamada {
    const id = ++this.sequencia;
    history.pushState({ ...(history.state ?? {}), camada: id }, '');
    this.pilha.push({ id, fechar });

    return (opcoes) => {
      const i = this.pilha.findIndex(c => c.id === id);
      if (i < 0) return; // já fechada pelo próprio voltar
      this.pilha.splice(i, 1);
      // Só desfaz a entrada se ela ainda for a atual: depois de uma navegação,
      // voltar tiraria o usuário da página nova.
      if (!opcoes?.semVoltar && history.state?.camada === id) {
        this.ignorar++;
        history.back();
      }
    };
  }

  /** Todo diálogo do Material passa a fechar com o voltar do celular. */
  iniciarDialogos(dialog: MatDialog): void {
    if (this.dialogosIniciados) return;
    this.dialogosIniciados = true;
    dialog.afterOpened.subscribe(ref => {
      const liberar = this.abrir(() => ref.close());
      ref.afterClosed().subscribe(() => liberar());
    });
  }

  private aoVoltar(): void {
    if (this.ignorar > 0) { this.ignorar--; return; }
    const topo = this.pilha.pop();
    if (topo) this.zone.run(() => topo.fechar());
  }
}
