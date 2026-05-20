import { Component, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { AbstractControl, ReactiveFormsModule, FormBuilder, ValidationErrors, ValidatorFn, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
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
  selector: 'app-primeiro-acesso',
  standalone: true,
  imports: [
    CommonModule, ReactiveFormsModule,
    MatCardModule, MatFormFieldModule, MatInputModule,
    MatButtonModule, MatIconModule, MatSnackBarModule, MatProgressSpinnerModule
  ],
  templateUrl: './primeiro-acesso.component.html',
  styleUrls: ['./primeiro-acesso.component.scss']
})
export class PrimeiroAcessoComponent {
  busy     = signal(false);
  hidePass = true;
  user     = this.auth.user;

  form = this.fb.group({
    email:           ['', [Validators.required, Validators.pattern(UDF_EMAIL_PATTERN)]],
    newPassword:     ['', [Validators.required, Validators.minLength(8)]],
    confirmPassword: ['', Validators.required]
  }, { validators: passwordMatchValidator() });

  constructor(
    private fb: FormBuilder,
    private auth: AuthService,
    private router: Router,
    private snackBar: MatSnackBar
  ) {
    // Pre-fill email if already set
    const current = this.auth.user();
    if (current?.email && UDF_EMAIL_PATTERN.test(current.email)) {
      this.form.get('email')?.setValue(current.email);
    }
  }

  onSubmit(): void {
    if (this.form.invalid) return;
    this.busy.set(true);
    const { email, newPassword } = this.form.value;
    this.auth.firstAccess(email!, newPassword!).subscribe({
      next: () => {
        this.busy.set(false);
        this.snackBar.open('Cadastro concluído! Bem-vindo ao EstágioCheck.', '', { duration: 3000, panelClass: 'snack-success' });
        this.router.navigate(['/app']);
      },
      error: (err) => {
        this.busy.set(false);
        this.snackBar.open(err?.error?.message ?? 'Erro ao concluir cadastro', '', { duration: 4000, panelClass: 'snack-error' });
      }
    });
  }
}
