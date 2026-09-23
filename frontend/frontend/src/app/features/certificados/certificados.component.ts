import { Component, OnInit, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTableModule } from '@angular/material/table';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatChipsModule } from '@angular/material/chips';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { CertificatesService } from '../../core/services/certificates.service';
import { AuthService } from '../../core/services/auth.service';
import { Certificate } from '../../core/models/models';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatPaginatorModule } from '@angular/material/paginator';
import { Paginacao, normalizarBusca } from '../../core/utils/paginacao';
import { mensagemErro } from '../../core/utils/api-error';

@Component({
  selector: 'app-certificados',
  standalone: true,
  imports: [
    CommonModule, MatCardModule, MatButtonModule, MatIconModule, MatTableModule,
    MatProgressSpinnerModule, MatProgressBarModule, MatChipsModule, MatSnackBarModule,
    MatFormFieldModule, MatInputModule, MatSelectModule, MatPaginatorModule
  ],
  templateUrl: './certificados.component.html',
  styleUrls: ['./certificados.component.scss']
})
export class CertificadosComponent implements OnInit {
  loading = signal(true);
  myCert = signal<Certificate | null>(null);
  list = signal<Certificate[]>([]);
  selected = signal<Certificate | null>(null);
  erro = signal<string | null>(null);

  readonly busca = signal('');
  readonly situacao = signal<'todos' | 'concluidos' | 'andamento'>('todos');
  readonly filtrados = computed(() => {
    const termo = normalizarBusca(this.busca());
    const situacao = this.situacao();
    return this.list().filter(c =>
      (situacao === 'todos' || c.eligible === (situacao === 'concluidos'))
      && (!termo || [c.studentName, c.rgm, c.groupName].some(v => normalizarBusca(v).includes(termo))));
  });
  readonly concluidos = computed(() => this.filtrados().filter(c => c.eligible).length);
  readonly paginacao = new Paginacao(this.filtrados);

  role = this.auth.role;
  isAluno = computed(() => this.role() === 'aluno');
  displayedColumns = ['name', 'group', 'hours', 'status', 'actions'];

  constructor(
    private certs: CertificatesService,
    private auth: AuthService,
    private snack: MatSnackBar
  ) {}

  ngOnInit(): void {
    if (this.isAluno()) {
      this.certs.me().subscribe({
        next: (c) => { this.myCert.set(c); this.loading.set(false); },
        error: (err) => this.falhou(err)
      });
    } else {
      this.certs.all().subscribe({
        next: (l) => { this.list.set(l); this.loading.set(false); },
        error: (err) => this.falhou(err)
      });
    }
  }

  private falhou(err: unknown): void {
    this.loading.set(false);
    this.erro.set(mensagemErro(err, 'Não foi possível carregar os certificados.'));
    this.snack.open(this.erro()!, 'OK', { duration: 6000, panelClass: 'snack-error' });
  }

  abrir(cert: Certificate): void {
    if (!cert.eligible) {
      this.snack.open('Carga horária ainda não concluída — certificado indisponível.', '', { duration: 3500 });
      return;
    }
    this.selected.set(cert);
  }

  voltar(): void { this.selected.set(null); }

  /** Abre o certificado em nova janela, formatado para impressão / salvar em PDF. */
  imprimir(cert: Certificate): void {
    const win = window.open('', '_blank', 'width=900,height=650');
    if (!win) {
      this.snack.open('Permita pop-ups para imprimir o certificado.', '', { duration: 3500 });
      return;
    }
    win.document.write(this.montarHtml(cert));
    win.document.close();
    win.focus();
    setTimeout(() => win.print(), 300);
  }

  private montarHtml(c: Certificate): string {
    const emissao = new Date(c.issuedAt).toLocaleDateString('pt-BR');
    const identificacao = c.rgm ? `RGM ${c.rgm}` : '';
    const locais = c.locations?.length ? c.locations.join(', ') : '—';
    const periodo = c.periodLabel ?? '—';
    const instituicao = c.institution || 'Centro Universitário do Distrito Federal – UDF';
    const horas = c.completedHours.toLocaleString('pt-BR');
    const exigidas = c.requiredHours.toLocaleString('pt-BR');

    return `<!doctype html>
<html lang="pt-BR"><head><meta charset="utf-8"><title>Certificado — ${c.studentName}</title>
<style>
  * { box-sizing: border-box; }
  body { font-family: Georgia, 'Times New Roman', serif; margin: 0; padding: 40px; color: #1a2b3c; }
  .cert { border: 8px double #00ADEE; border-radius: 12px; padding: 48px 56px; max-width: 820px; margin: 0 auto; }
  .titulo { text-align: center; font-size: 34px; letter-spacing: 6px; font-weight: bold; color: #0a3d62; margin-bottom: 8px; }
  .sub { text-align: center; font-size: 14px; color: #6B7280; margin-bottom: 36px; text-transform: uppercase; letter-spacing: 2px; }
  .corpo { font-size: 18px; line-height: 1.9; text-align: justify; }
  .nome { font-weight: bold; }
  .destaque { color: #0a3d62; font-weight: bold; }
  .rodape { margin-top: 48px; display: flex; justify-content: space-between; font-size: 13px; color: #6B7280; }
  .codigo { margin-top: 28px; text-align: center; font-size: 12px; color: #9aa5b1; }
  @media print { body { padding: 0; } .cert { border-color: #00ADEE; } }
</style></head>
<body>
  <div class="cert">
    <div class="titulo">CERTIFICADO</div>
    <div class="sub">Estágio Supervisionado em Enfermagem</div>
    <div class="corpo">
      Certificamos que <span class="nome">${c.studentName}</span>${identificacao ? `, ${identificacao},` : ','}
      cumpriu <span class="destaque">${horas} horas</span> das <span class="destaque">${exigidas} horas</span>
      exigidas de estágio supervisionado${c.groupName ? ` (turma ${c.groupName})` : ''},
      no período de <span class="destaque">${periodo}</span>,
      nos seguintes campos de prática: ${locais}.
    </div>
    <div class="rodape">
      <span>${instituicao}</span>
      <span>Emitido em ${emissao}</span>
    </div>
    <div class="codigo">Código de verificação: ${c.verificationCode}</div>
  </div>
</body></html>`;
  }
}
