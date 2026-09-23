import { Component, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatIconModule } from '@angular/material/icon';
import { AuthService } from '../../core/services/auth.service';
import { mensagemErro } from '../../core/utils/api-error';

@Component({
  selector: 'app-auth',
  standalone: true,
  imports: [
    CommonModule, ReactiveFormsModule,
    MatCardModule, MatFormFieldModule, MatInputModule, MatButtonModule,
    MatSnackBarModule, MatProgressSpinnerModule,
    MatIconModule
  ],
  templateUrl: './auth.component.html',
  styleUrls: ['./auth.component.scss']
})
export class AuthComponent {
  busy       = signal(false);
  /** 0 = login; 1 = orientação para quem esqueceu a senha. */
  view       = signal(0);
  hidePass    = true;

  loginForm = this.fb.group({
    email:    ['', [Validators.required]],
    password: ['', [Validators.required, Validators.minLength(6)]]
  });

  constructor(
    private fb: FormBuilder,
    private auth: AuthService,
    private router: Router,
    private snackBar: MatSnackBar
  ) {}

  goToForgot(): void { this.view.set(1); }
  goToLogin():  void { this.view.set(0); }

  onLogin(): void {
    if (this.loginForm.invalid) return;
    this.busy.set(true);
    const { email, password } = this.loginForm.value;
    this.auth.login({ email: email!, password: password! }).subscribe({
      next: (res) => {
        this.busy.set(false);
        if (res.mustChangePassword || res.mustSetEmail) {
          this.router.navigate(['/primeiro-acesso']);
        } else {
          this.snackBar.open('Bem-vindo!', '', { duration: 2000, panelClass: 'snack-success' });
          this.router.navigate(['/app']);
        }
      },
      error: (err) => {
        this.busy.set(false);
        this.snackBar.open(mensagemErro(err, 'E-mail/senha incorretos'), '', { duration: 4000, panelClass: 'snack-error' });
      }
    });
  }

}
