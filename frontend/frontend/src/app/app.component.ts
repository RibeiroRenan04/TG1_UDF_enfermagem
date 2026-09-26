import { Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { MatDialog } from '@angular/material/dialog';
import { VoltarService } from './core/services/voltar.service';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet],
  template: '<router-outlet />'
})
export class AppComponent {
  constructor() {
    // O voltar do celular fecha o diálogo aberto em vez de sair da tela.
    inject(VoltarService).iniciarDialogos(inject(MatDialog));
  }
}
