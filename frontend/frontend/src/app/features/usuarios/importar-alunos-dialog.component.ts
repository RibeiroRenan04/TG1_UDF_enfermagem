import { Component, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatSelectModule } from '@angular/material/select';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatDividerModule } from '@angular/material/divider';
import * as XLSX from 'xlsx';
import { UsersService } from '../../core/services/users.service';
import { BulkImportStudent, BulkImportResult, ImportedStudentLogin } from '../../core/models/models';

@Component({
  selector: 'app-importar-alunos-dialog',
  standalone: true,
  imports: [
    CommonModule, FormsModule,
    MatDialogModule, MatButtonModule, MatSelectModule,
    MatFormFieldModule, MatIconModule, MatProgressSpinnerModule, MatDividerModule
  ],
  template: `
    <h2 mat-dialog-title>{{ resultado() ? 'Importação concluída' : 'Importar lista de alunos' }}</h2>

    <!-- ── Resultado: logins gerados ──────────────────────────────────────── -->
    <mat-dialog-content *ngIf="resultado() as res">
      <div class="resumo">
        <span class="badge ok">{{ res.imported }} importado(s)</span>
        <span class="badge">{{ res.updated }} atualizado(s)</span>
        <span class="badge erro" *ngIf="res.errors.length">{{ res.errors.length }} erro(s)</span>
      </div>

      <div class="erros" *ngIf="res.errors.length">
        <p *ngFor="let e of res.errors">{{ e }}</p>
      </div>

      <ng-container *ngIf="res.logins.length; else semLogins">
        <div class="credenciais-box">
          <mat-icon>vpn_key</mat-icon>
          <div>
            Estes são os <strong>logins gerados</strong>. A senha inicial de cada aluno é o
            <strong>RGM</strong>, trocada no primeiro acesso.
          </div>
        </div>

        <table class="logins">
          <thead><tr><th>Nome</th><th>RGM</th><th>Login (e-mail)</th></tr></thead>
          <tbody>
            <tr *ngFor="let l of res.logins">
              <td>{{ l.fullName }}</td>
              <td>{{ l.rgm }}</td>
              <td class="email">{{ l.email }}</td>
            </tr>
          </tbody>
        </table>
      </ng-container>

      <ng-template #semLogins>
        <p class="hint">Nenhum login novo foi gerado — os alunos importados já possuíam e-mail.</p>
      </ng-template>
    </mat-dialog-content>

    <mat-dialog-actions align="end" *ngIf="resultado() as res">
      <button mat-stroked-button *ngIf="res.logins.length" (click)="baixarLogins(res.logins)">
        <mat-icon>download</mat-icon> Baixar planilha
      </button>
      <button mat-raised-button color="primary" (click)="dialogRef.close(res)">Concluir</button>
    </mat-dialog-actions>

    <!-- ── Envio ──────────────────────────────────────────────────────────── -->
    <mat-dialog-content *ngIf="!resultado()">
      <p class="hint">
        Envie um arquivo <strong>Excel (.xlsx)</strong> ou <strong>CSV</strong> com as colunas
        <code>RGM</code> e <code>Nome</code> (opcionalmente <code>Semestre</code> e <code>Turno</code>).
      </p>

      <p class="hint-small rgm-note">
        O <strong>&quot;14&quot; do início do RGM</strong> não é mais usado e é removido
        automaticamente na importação — a planilha pode vir em qualquer um dos dois formatos.
      </p>

      <div class="credenciais-box">
        <mat-icon>vpn_key</mat-icon>
        <div>
          <strong>Como o aluno entra no sistema</strong>
          <ul>
            <li><strong>Login:</strong> e-mail institucional gerado automaticamente no formato
              <code>primeironome.ultimosobrenome&#64;cs.udf.edu.br</code>.</li>
            <li><strong>Senha inicial:</strong> o próprio <strong>RGM</strong> informado na planilha.</li>
            <li>No primeiro acesso o aluno confirma o e-mail e define uma senha nova.</li>
          </ul>
          Depois de importar, use <strong>Exportar credenciais</strong> na tela de Usuários para
          entregar login e senha a cada aluno.
        </div>
      </div>

      <div class="defaults-row">
        <mat-form-field appearance="outline">
          <mat-label>Semestre padrão</mat-label>
          <mat-select [(ngModel)]="defaultSemester">
            <mat-option [value]="7">7° semestre</mat-option>
            <mat-option [value]="8">8° semestre</mat-option>
          </mat-select>
        </mat-form-field>

        <mat-form-field appearance="outline">
          <mat-label>Turno padrão</mat-label>
          <mat-select [(ngModel)]="defaultShift">
            <mat-option value="manha">Manhã</mat-option>
            <mat-option value="tarde">Tarde</mat-option>
            <mat-option value="noite">Noite</mat-option>
          </mat-select>
        </mat-form-field>
      </div>

      <p class="hint-small">Usado quando a planilha não trouxer essas colunas.</p>

      <div class="file-area" (click)="fileInput.click()" [class.has-file]="fileName">
        <mat-icon>upload_file</mat-icon>
        <span>{{ fileName || 'Clique para escolher arquivo' }}</span>
        <input #fileInput type="file" accept=".xlsx,.xls,.csv" style="display:none" (change)="onFileChange($event)">
      </div>

      <div *ngIf="preview.length" class="preview">
        <mat-divider></mat-divider>
        <p class="preview-label">Pré-visualização ({{ preview.length }} registro(s)):</p>
        <table>
          <thead><tr><th>RGM</th><th>Nome</th><th>Semestre</th><th>Turno</th></tr></thead>
          <tbody>
            <tr *ngFor="let r of preview.slice(0,5)">
              <td>{{ r.rgm }}</td>
              <td>{{ r.fullName }}</td>
              <td>{{ r.semester }}°</td>
              <td>{{ r.shift }}</td>
            </tr>
            <tr *ngIf="preview.length > 5"><td colspan="4" style="text-align:center;color:#6B7280">...e mais {{ preview.length - 5 }} registro(s)</td></tr>
          </tbody>
        </table>
      </div>
    </mat-dialog-content>

    <mat-dialog-actions align="end" *ngIf="!resultado()">
      <button mat-button (click)="dialogRef.close(null)">Fechar</button>
      <button mat-raised-button color="primary"
              [disabled]="!preview.length || busy()"
              (click)="onImport()">
        <mat-spinner *ngIf="busy()" diameter="18" style="display:inline-block;margin-right:8px"></mat-spinner>
        <span *ngIf="!busy()">Importar {{ preview.length }} aluno(s)</span>
      </button>
    </mat-dialog-actions>
  `,
  styles: [`
    .hint { font-size: 0.85rem; color: #6B7280; margin-bottom: 12px; }
    .credenciais-box {
      display: flex; gap: 8px; align-items: flex-start;
      background: #eff6ff; border: 1px solid #bfdbfe; color: #1e40af;
      border-radius: 8px; padding: 10px 12px; margin-bottom: 16px;
      font-size: 0.78rem; line-height: 1.5;
      mat-icon { font-size: 20px; width: 20px; height: 20px; flex-shrink: 0; }
      ul { margin: 4px 0; padding-left: 18px; }
      code { background: #dbeafe; }
    }
    .hint-small { font-size: 0.75rem; color: #9ca3af; margin: -8px 0 12px; }
    .rgm-note { margin: 0 0 14px; color: #6B7280; }
    .resumo { display: flex; gap: 8px; flex-wrap: wrap; margin-bottom: 12px; }
    .badge {
      font-size: 0.75rem; padding: 3px 10px; border-radius: 9999px;
      background: #f3f4f6; color: #374151;
      &.ok { background: #dcfce7; color: #166534; }
      &.erro { background: #fee2e2; color: #991b1b; }
    }
    .erros {
      background: #fef2f2; border: 1px solid #fecaca; border-radius: 8px;
      padding: 8px 10px; margin-bottom: 12px;
      p { margin: 2px 0; font-size: 0.76rem; color: #991b1b; }
    }
    .logins { width: 100%; border-collapse: collapse; font-size: 0.8rem; }
    .logins th { text-align: left; padding: 5px 8px; background: #f3f4f6; color: #374151; position: sticky; top: 0; }
    .logins td { padding: 4px 8px; border-bottom: 1px solid #f3f4f6; }
    .logins .email { font-family: ui-monospace, monospace; font-size: 0.76rem; }
    code { background: #f3f4f6; padding: 1px 4px; border-radius: 4px; font-size: 0.8rem; }
    .defaults-row { display: flex; gap: 12px; }
    .defaults-row mat-form-field { flex: 1; }
    .file-area {
      border: 2px dashed #d1d5db; border-radius: 8px;
      padding: 24px; text-align: center; cursor: pointer;
      display: flex; flex-direction: column; align-items: center; gap: 6px;
      color: #6B7280; transition: border-color .2s;
      mat-icon { font-size: 36px; height: 36px; width: 36px; }
      &:hover, &.has-file { border-color: #0056A6; color: #0056A6; }
    }
    .preview { margin-top: 12px; }
    .preview-label { font-size: 0.8rem; color: #6B7280; margin: 8px 0 4px; }
    table { width: 100%; border-collapse: collapse; font-size: 0.82rem; }
    th { text-align: left; padding: 4px 8px; background: #f3f4f6; color: #374151; }
    td { padding: 3px 8px; border-bottom: 1px solid #f3f4f6; }
  `]
})
export class ImportarAlunosDialogComponent {
  defaultSemester: 7 | 8 = 7;
  defaultShift: 'manha' | 'tarde' | 'noite' = 'manha';
  fileName = '';
  preview: BulkImportStudent[] = [];
  busy = signal(false);
  /** Resultado da importação: exibe os logins gerados antes de fechar. */
  resultado = signal<BulkImportResult | null>(null);

  constructor(
    public dialogRef: MatDialogRef<ImportarAlunosDialogComponent>,
    private usersService: UsersService
  ) {}

  onFileChange(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file  = input.files?.[0];
    if (!file) return;
    this.fileName = file.name;
    const reader  = new FileReader();

    if (file.name.endsWith('.csv')) {
      reader.onload = (e) => {
        const text = e.target?.result as string;
        this.preview = this.parseCsv(text);
      };
      reader.readAsText(file, 'UTF-8');
    } else {
      reader.onload = (e) => {
        const data = new Uint8Array(e.target?.result as ArrayBuffer);
        const wb   = XLSX.read(data, { type: 'array' });
        const ws   = wb.Sheets[wb.SheetNames[0]];
        const rows = XLSX.utils.sheet_to_json<Record<string, string>>(ws, { defval: '' });
        this.preview = this.mapRows(rows);
      };
      reader.readAsArrayBuffer(file);
    }
  }

  private parseCsv(text: string): BulkImportStudent[] {
    const lines  = text.split(/\r?\n/).filter(l => l.trim());
    if (!lines.length) return [];
    const headers = lines[0].split(/[;,]/).map(h => h.trim().toLowerCase());
    return lines.slice(1).map(line => {
      const cols = line.split(/[;,]/);
      const row: Record<string, string> = {};
      headers.forEach((h, i) => row[h] = (cols[i] ?? '').trim());
      return this.rowToStudent(row);
    }).filter(s => !!s.rgm);
  }

  private mapRows(rows: Record<string, string>[]): BulkImportStudent[] {
    return rows.map(r => {
      const normalized: Record<string, string> = {};
      Object.keys(r).forEach(k => normalized[k.toLowerCase().trim()] = String(r[k]).trim());
      return this.rowToStudent(normalized);
    }).filter(s => !!s.rgm);
  }

  /**
   * Padroniza o RGM: mantém apenas dígitos e remove o "14" do início, que deixou de
   * fazer parte do formato usado na faculdade. Planilhas antigas continuam válidas.
   */
  private normalizarRgm(valor: string): string {
    const digitos = (valor ?? '').replace(/\D/g, '');
    return digitos.startsWith('14') && digitos.length > 2 ? digitos.slice(2) : digitos;
  }

  private rowToStudent(row: Record<string, string>): BulkImportStudent {
    const rgm      = this.normalizarRgm(row['rgm'] || row['matricula'] || row['login'] || '');
    const fullName = row['nome'] || row['name'] || row['fullname'] || '';
    const semRaw   = row['semestre'] || row['semester'] || '';
    const shiftRaw = (row['turno'] || row['shift'] || '').toLowerCase();
    const semNum   = parseInt(semRaw, 10);
    const semester: 7 | 8 = (semNum === 7 || semNum === 8) ? semNum : this.defaultSemester;
    const shift    = (['manha','tarde','noite'].includes(shiftRaw) ? shiftRaw : this.defaultShift) as 'manha'|'tarde'|'noite';
    return { rgm, fullName, semester, shift };
  }

  onImport(): void {
    if (!this.preview.length) return;
    this.busy.set(true);
    this.usersService.bulkImportStudents(this.preview).subscribe({
      next: (res) => {
        this.busy.set(false);
        // Mostra os logins gerados antes de fechar: é a única vez que o
        // supervisor vê com que e-mail cada aluno passa a entrar.
        this.resultado.set({ ...res, errors: res.errors ?? [], logins: res.logins ?? [] });
      },
      error: (err) => {
        this.busy.set(false);
        console.error('Erro ao importar:', err);
      }
    });
  }

  /** Planilha com os logins recém-gerados, para entregar à turma. */
  baixarLogins(logins: ImportedStudentLogin[]): void {
    const linhas = logins.map(l => ({
      'Nome': l.fullName,
      'RGM': l.rgm,
      'Login (e-mail)': l.email,
      'Senha inicial': l.rgm
    }));
    const ws = XLSX.utils.json_to_sheet(linhas);
    ws['!cols'] = [{ wch: 32 }, { wch: 12 }, { wch: 34 }, { wch: 14 }];
    const wb = XLSX.utils.book_new();
    XLSX.utils.book_append_sheet(wb, ws, 'Logins');
    XLSX.writeFile(wb, `logins-alunos-${new Date().toISOString().substring(0, 10)}.xlsx`);
  }
}
