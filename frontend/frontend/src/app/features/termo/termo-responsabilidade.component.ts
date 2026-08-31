import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { AuthService } from '../../core/services/auth.service';
import { ResponsibilityTerms } from '../../core/models/models';

/**
 * Termo de responsabilidade de acesso, exigido de preceptores, professores e
 * coordenadoras antes do primeiro uso. Registra que a senha não deve ser
 * compartilhada e que o usuário responde pelas ações feitas com a conta.
 */
@Component({
  selector: 'app-termo-responsabilidade',
  standalone: true,
  imports: [
    CommonModule, FormsModule, MatCardModule, MatButtonModule,
    MatIconModule, MatCheckboxModule, MatProgressSpinnerModule, MatSnackBarModule
  ],
  templateUrl: './termo-responsabilidade.component.html',
  styleUrls: ['./termo-responsabilidade.component.scss']
})
export class TermoResponsabilidadeComponent implements OnInit {
  termo = signal<ResponsibilityTerms | null>(null);
  loading = signal(true);
  busy = signal(false);
  aceito = false;

  user = this.auth.user;

  constructor(
    private auth: AuthService,
    private router: Router,
    private snackBar: MatSnackBar
  ) {}

  ngOnInit(): void {
    this.auth.getTerms().subscribe({
      next: (t) => { this.termo.set(t); this.loading.set(false); },
      error: () => { this.termo.set(this.termoPadrao()); this.loading.set(false); }
    });
  }

  confirmar(): void {
    if (!this.aceito) return;
    this.busy.set(true);
    this.auth.acceptTerms().subscribe({
      next: () => {
        this.busy.set(false);
        this.snackBar.open('Termo aceito. Bom trabalho!', '', { duration: 3000, panelClass: 'snack-success' });
        this.router.navigate(['/app']);
      },
      error: (err) => {
        this.busy.set(false);
        this.snackBar.open(err?.error?.message ?? 'Erro ao registrar o aceite', '',
          { duration: 4000, panelClass: 'snack-error' });
      }
    });
  }

  sair(): void { this.auth.logout(); }

  /** Fallback caso a API não responda: o conteúdo do termo não pode faltar. */
  private termoPadrao(): ResponsibilityTerms {
    return {
      titulo: 'Termo de Responsabilidade de Acesso',
      versao: '1.0',
      itens: [
        'A senha de acesso é pessoal e intransferível: não deve ser compartilhada com alunos, colegas ou terceiros, em nenhuma hipótese.',
        'Sou responsável por todas as ações realizadas no sistema com a minha conta, incluindo lançamentos, validações e alterações de dados.',
        'Os dados de alunos acessados aqui são de uso restrito e acadêmico, e não podem ser divulgados fora das atividades de estágio.',
        'Devo encerrar a sessão ao terminar o uso, especialmente em computadores compartilhados.',
        'Comunicarei imediatamente à coordenação qualquer suspeita de uso indevido da minha conta e solicitarei a troca da senha.'
      ]
    };
  }
}
