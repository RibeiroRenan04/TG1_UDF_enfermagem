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
import { BulkImportStudent, BulkImportResult } from '../../core/models/models';

@Component({
  selector: 'app-importar-alunos-dialog',
  standalone: true,
  imports: [
    CommonModule, FormsModule,
    MatDialogModule, MatButtonModule, MatSelectModule,
    MatFormFieldModule, MatIconModule, MatProgressSpinnerModule, MatDividerModule
  ],
  template: `
    <h2 mat-dialog-title>Importar lista de alunos</h2>
    <mat-dialog-content>
      <p class="hint">
        Envie um arquivo <strong>Excel (.xlsx)</strong> ou <strong>CSV</strong> com as colunas
        <code>RGM</code> e <code>Nome</code> (opcionalmente <code>Semestre</code> e <code>Turno</code>).<br>
        Alunos novos recebem a senha igual ao RGM e deverão alterá-la no primeiro acesso.
      </p>

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

    <mat-dialog-actions align="end">
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
    .hint-small { font-size: 0.75rem; color: #9ca3af; margin: -8px 0 12px; }
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

  private rowToStudent(row: Record<string, string>): BulkImportStudent {
    const rgm      = row['rgm'] || row['matricula'] || row['login'] || '';
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
      next: (res) => { this.busy.set(false); this.dialogRef.close(res); },
      error: (err) => {
        this.busy.set(false);
        console.error('Erro ao importar:', err);
      }
    });
  }
}
