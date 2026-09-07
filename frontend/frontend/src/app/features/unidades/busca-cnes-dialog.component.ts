import { Component, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatIconModule } from '@angular/material/icon';
import { MatTableModule } from '@angular/material/table';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { LocationsService } from '../../core/services/locations.service';
import { BuscaSaudeEstabelecimento } from '../../core/models/models';

/**
 * Busca de unidades de saúde do DF no CNES/OpenDataSUS e importação delas como
 * unidades do estágio.
 *
 * Antes esta busca vivia na tela "Locais", que listava e editava a MESMA tabela
 * de "Unidades de saúde" — duas telas para o mesmo cadastro. A busca virou este
 * diálogo dentro de Unidades e a tela duplicada saiu do ar.
 */
@Component({
  selector: 'app-busca-cnes-dialog',
  standalone: true,
  imports: [
    CommonModule, FormsModule,
    MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule,
    MatIconModule, MatTableModule, MatProgressSpinnerModule
  ],
  template: `
    <h2 mat-dialog-title>Buscar unidade de saúde no CNES</h2>

    <mat-dialog-content>
      <p class="ajuda">
        Unidades de saúde do DF (UBS, hospitais, UPAs) na API pública do
        CNES/OpenDataSUS. A unidade importada entra no cadastro com as coordenadas
        do CNES — as mesmas que validam o check-in do aluno.
      </p>

      <form (ngSubmit)="buscar()" class="busca">
        <mat-form-field appearance="outline" subscriptSizing="dynamic" class="campo">
          <mat-label>Nome da unidade (ex.: UBS, hospital, nome do bairro)</mat-label>
          <input matInput [(ngModel)]="termo" name="termo" [disabled]="buscando()">
          <mat-icon matSuffix>search</mat-icon>
        </mat-form-field>
        <button mat-raised-button color="primary" type="submit" [disabled]="buscando()">Buscar</button>
      </form>

      <div *ngIf="buscando()" class="carregando"><mat-spinner diameter="32"></mat-spinner></div>

      <table mat-table [dataSource]="resultados()" class="full-width"
             *ngIf="!buscando() && resultados().length; else vazio">
        <ng-container matColumnDef="nome">
          <th mat-header-cell *matHeaderCellDef>Unidade</th>
          <td mat-cell *matCellDef="let e">{{ e.nome }}</td>
        </ng-container>
        <ng-container matColumnDef="endereco">
          <th mat-header-cell *matHeaderCellDef>Endereço</th>
          <td mat-cell *matCellDef="let e">{{ e.endereco }}</td>
        </ng-container>
        <ng-container matColumnDef="telefone">
          <th mat-header-cell *matHeaderCellDef>Telefone</th>
          <td mat-cell *matCellDef="let e">{{ e.telefone || '—' }}</td>
        </ng-container>
        <ng-container matColumnDef="acoes">
          <th mat-header-cell *matHeaderCellDef></th>
          <td mat-cell *matCellDef="let e">
            <button mat-stroked-button color="primary" (click)="importar(e)"
                    [disabled]="importado(e) || importandoCnes() === e.codigoCnes">
              <mat-icon>{{ importado(e) ? 'check' : 'download' }}</mat-icon>
              {{ importado(e) ? 'Importada' : 'Importar' }}
            </button>
          </td>
        </ng-container>
        <tr mat-header-row *matHeaderRowDef="colunas"></tr>
        <tr mat-row *matRowDef="let row; columns: colunas;"></tr>
      </table>

      <ng-template #vazio>
        <div class="empty-state" *ngIf="!buscando()">
          <mat-icon>search_off</mat-icon>
          <p>
            {{ buscou()
                ? 'Nenhuma unidade encontrada para esse termo.'
                : 'Deixe em branco para listar as UBS ou digite um termo e clique em Buscar.' }}
          </p>
        </div>
      </ng-template>

      <p class="erro" *ngIf="erro()">{{ erro() }}</p>
    </mat-dialog-content>

    <mat-dialog-actions align="end">
      <button mat-button (click)="dialogRef.close(importouAlguma)">Fechar</button>
    </mat-dialog-actions>
  `,
  styles: [`
    :host { display: block; min-width: 620px; }
    .ajuda { font-size: 0.8rem; color: #6B7280; line-height: 1.5; margin: 0 0 14px; }
    .busca { display: flex; gap: 10px; align-items: center; margin-bottom: 14px; }
    .campo { flex: 1; }
    .carregando { display: flex; justify-content: center; padding: 28px; }
    .full-width { width: 100%; }
    .empty-state {
      text-align: center; color: #6B7280; padding: 28px 8px;
      mat-icon { font-size: 38px; width: 38px; height: 38px; color: #d1d5db; }
      p { font-size: 0.85rem; margin: 8px 0 0; }
    }
    .erro { color: #b91c1c; font-size: 0.85rem; margin: 10px 0 0; }
  `]
})
export class BuscaCnesDialogComponent {
  termo = '';
  buscando = signal(false);
  buscou = signal(false);
  erro = signal('');
  resultados = signal<BuscaSaudeEstabelecimento[]>([]);
  importandoCnes = signal<string | null>(null);
  importouAlguma = false;

  colunas = ['nome', 'endereco', 'telefone', 'acoes'];

  /** CNES já presentes no cadastro — o botão de importar fica inativo neles. */
  private jaCadastrados = signal<Set<string>>(new Set());

  constructor(
    public dialogRef: MatDialogRef<BuscaCnesDialogComponent>,
    private locationsService: LocationsService
  ) {
    this.locationsService.getAll().subscribe({
      next: (l) => this.jaCadastrados.set(
        new Set(l.map(x => x.codigoCnes).filter((c): c is string => !!c))),
      error: () => {}
    });
  }

  buscar(): void {
    this.buscando.set(true);
    this.erro.set('');
    this.locationsService.buscaSaude(this.termo).subscribe({
      next: (r) => { this.resultados.set(r); this.buscando.set(false); this.buscou.set(true); },
      error: () => {
        this.buscando.set(false);
        this.buscou.set(true);
        this.erro.set('Erro ao consultar o CNES. Tente novamente em instantes.');
      }
    });
  }

  importado(e: BuscaSaudeEstabelecimento): boolean {
    return this.jaCadastrados().has(e.codigoCnes);
  }

  importar(e: BuscaSaudeEstabelecimento): void {
    this.importandoCnes.set(e.codigoCnes);
    this.erro.set('');
    this.locationsService.importFromBuscaSaude(e).subscribe({
      next: () => {
        this.importandoCnes.set(null);
        this.importouAlguma = true;
        this.jaCadastrados.update(s => new Set(s).add(e.codigoCnes));
      },
      error: (err) => {
        this.importandoCnes.set(null);
        // 409 significa que já está no cadastro: marcamos como importada.
        if (err?.status === 409) {
          this.jaCadastrados.update(s => new Set(s).add(e.codigoCnes));
          return;
        }
        this.erro.set(err?.error?.message ?? 'Erro ao importar a unidade.');
      }
    });
  }
}
