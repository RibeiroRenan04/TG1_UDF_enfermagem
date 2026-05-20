import { Component, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { AbstractControl, ReactiveFormsModule, FormBuilder, ValidationErrors, ValidatorFn, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatSelectModule } from '@angular/material/select';
import { MatTabsModule } from '@angular/material/tabs';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatIconModule } from '@angular/material/icon';
import { AuthService } from '../../core/services/auth.service';

const UDF_EMAIL_PATTERN = /^[a-zA-Z0-9._%+\-]+@cs\.udf\.edu\.br$/;

function passwordMatchValidator(): ValidatorFn {
  return (group: AbstractControl): ValidationErrors | null => {
    const pw  = group.get('newPassword')?.value;
    const cpw = group.get('confirmPassword')?.value;
    return pw && cpw && pw !== cpw ? { mismatch: true } : null;
  };
}

@Component({
  selector: 'app-auth',
  standalone: true,
  imports: [
    CommonModule, ReactiveFormsModule,
    MatCardModule, MatFormFieldModule, MatInputModule, MatButtonModule,
    MatSelectModule, MatTabsModule, MatSnackBarModule, MatProgressSpinnerModule,
    MatIconModule
  ],
  templateUrl: './auth.component.html',
  styleUrls: ['./auth.component.scss']
})
export class AuthComponent {
  busy       = signal(false);
  view       = signal(0);   // 0=login 1=forgotEmail 2=forgotCode 3=resetPw
  selectedTab = 0;
  hidePass    = true;
  hideNewPass = true;

  loginForm = this.fb.group({
    email:    ['', [Validators.required]],
    password: ['', [Validators.required, Validators.minLength(6)]]
  });

  registerForm = this.fb.group({
    fullName:  ['', [Validators.required, Validators.minLength(2)]],
    email:     ['', [Validators.required, Validators.pattern(UDF_EMAIL_PATTERN)]],
    password:  ['', [Validators.required, Validators.minLength(6)]],
    matricula: [''],
    role:      ['aluno', Validators.required]
  });

  forgotForm = this.fb.group({
    forgotEmail: ['', [Validators.required, Validators.pattern(UDF_EMAIL_PATTERN)]]
  });

  codeForm = this.fb.group({
    code: ['', [Validators.required, Validators.minLength(6), Validators.maxLength(6)]]
  });

  resetForm = this.fb.group({
    newPassword:     ['', [Validators.required, Validators.minLength(8)]],
    confirmPassword: ['', Validators.required]
  }, { validators: passwordMatchValidator() });

  constructor(
    private fb: FormBuilder,
    private auth: AuthService,
    private router: Router,
    private snackBar: MatSnackBar
  ) {}

  goToForgot(): void { this.view.set(1); }
  goToLogin():  void { this.view.set(0); this.forgotForm.reset(); this.codeForm.reset(); }

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
        this.snackBar.open(err?.error?.message ?? 'E-mail/senha incorretos', '', { duration: 4000, panelClass: 'snack-error' });
      }
    });
  }

  onRegister(): void {
    if (this.registerForm.invalid) return;
    this.busy.set(true);
    const v = this.registerForm.value;
    this.auth.register({
      fullName: v.fullName!,
      email: v.email!,
      password: v.password!,
      matricula: v.matricula || undefined,
      role: v.role!
    }).subscribe({
      next: () => { this.busy.set(false); this.snackBar.open('Conta criada!', '', { duration: 2000, panelClass: 'snack-success' }); this.router.navigate(['/app']); },
      error: (err) => { this.busy.set(false); this.snackBar.open(err?.error?.message ?? 'Erro ao cadastrar', '', { duration: 4000, panelClass: 'snack-error' }); }
    });
  }

  onSendCode(): void {
    const email = this.forgotForm.get('forgotEmail')?.value;
    if (!email) return;
    this.busy.set(true);
    this.auth.forgotPassword(email).subscribe({
      next: () => { this.busy.set(false); this.view.set(2); },
      error: (err) => { this.busy.set(false); this.snackBar.open(err?.error?.message ?? 'Erro ao enviar código', '', { duration: 4000, panelClass: 'snack-error' }); }
    });
  }

  onVerifyCode(): void {
    if (this.codeForm.invalid) return;
    const email = this.forgotForm.get('forgotEmail')?.value!;
    const code  = this.codeForm.get('code')?.value!;
    this.busy.set(true);
    this.auth.verifyResetCode(email, code).subscribe({
      next: () => { this.busy.set(false); this.view.set(3); },
      error: (err) => { this.busy.set(false); this.snackBar.open(err?.error?.message ?? 'Código inválido ou expirado', '', { duration: 4000, panelClass: 'snack-error' }); }
    });
  }

  onResetPassword(): void {
    if (this.resetForm.invalid) return;
    const email       = this.forgotForm.get('forgotEmail')?.value!;
    const code        = this.codeForm.get('code')?.value!;
    const newPassword = this.resetForm.get('newPassword')?.value!;
    this.busy.set(true);
    this.auth.resetPassword(email, code, newPassword).subscribe({
      next: () => {
        this.busy.set(false);
        this.snackBar.open('Senha redefinida com sucesso!', '', { duration: 3000, panelClass: 'snack-success' });
        this.goToLogin();
      },
      error: (err) => { this.busy.set(false); this.snackBar.open(err?.error?.message ?? 'Erro ao redefinir senha', '', { duration: 4000, panelClass: 'snack-error' }); }
    });
  }
}
