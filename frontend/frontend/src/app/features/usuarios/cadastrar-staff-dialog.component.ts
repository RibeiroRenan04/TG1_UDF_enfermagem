import { Component, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatSelectModule } from '@angular/material/select';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { UsersService } from '../../core/services/users.service';

@Component({
  selector: 'app-cadastrar-staff-dialog',
  standalone: true,
  imports: [
    CommonModule, ReactiveFormsModule,
    MatDialogModule, MatButtonModule, MatSelectModule,
    MatFormFieldModule, MatInputModule, MatProgressSpinnerModule
  ],
  template: `
    <h2 mat-dialog-title>Cadastrar preceptor ou supervisor</h2>
    <mat-dialog-content>
      <p class="hint">O usuário receberá a senha inicial informada e deverá trocá-la no primeiro acesso.</p>
      <form [formGroup]="form" class="form">
        <mat-form-field appearance="outline">
          <mat-label>Nome completo</mat-label>
          <input matInput formControlName="fullName">
        </mat-form-field>

        <div class="row-two">
          <mat-form-field appearance="outline">
            <mat-label>Tipo de perfil</mat-label>
            <mat-select formControlName="role">
              <mat-option value="preceptor">Preceptor</mat-option>
              <mat-option value="supervisor">Supervisor</mat-option>
            </mat-select>
          </mat-form-field>

          <mat-form-field appearance="outline">
            <mat-label>Vínculo institucional</mat-label>
            <input matInput formControlName="institution" placeholder="Ex.: UBS Asa Norte">
          </mat-form-field>
        </div>

        <mat-form-field appearance="outline">
          <mat-label>E-mail (login)</mat-label>
          <input matInput type="email" formControlName="email">
          <mat-error *ngIf="form.get('email')?.hasError('email')">E-mail inválido</mat-error>
        </mat-form-field>

        <div class="row-two">
          <mat-form-field appearance="outline">
            <mat-label>Senha inicial</mat-label>
            <input matInput type="password" formControlName="password" placeholder="Mínimo 6 caracteres">
            <mat-error *ngIf="form.get('password')?.hasError('minlength')">Mínimo 6 caracteres</mat-error>
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>Telefone (opcional)</mat-label>
            <input matInput formControlName="phone">
          </mat-form-field>
        </div>
      </form>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button (click)="dialogRef.close(false)">Cancelar</button>
      <button mat-raised-button color="primary" [disabled]="form.invalid || busy()" (click)="onSubmit()">
        <mat-spinner *ngIf="busy()" diameter="18" style="display:inline-block;margin-right:8px"></mat-spinner>
        <span *ngIf="!busy()">Cadastrar</span>
      </button>
    </mat-dialog-actions>
  `,
  styles: [`
    .hint { font-size: 0.85rem; color: #6B7280; margin-bottom: 8px; }
    .form { display: flex; flex-direction: column; gap: 8px; min-width: 380px; }
    .row-two { display: flex; gap: 12px; mat-form-field { flex: 1; } }
  `]
})
export class CadastrarStaffDialogComponent {
  busy = signal(false);

  form = this.fb.group({
    fullName:    ['', [Validators.required, Validators.minLength(2)]],
    role:        ['preceptor', Validators.required],
    institution: [''],
    email:       ['', [Validators.required, Validators.email]],
    password:    ['', [Validators.required, Validators.minLength(6)]],
    phone:       ['']
  });

  constructor(
    private fb: FormBuilder,
    public dialogRef: MatDialogRef<CadastrarStaffDialogComponent>,
    private usersService: UsersService
  ) {}

  onSubmit(): void {
    if (this.form.invalid) return;
    this.busy.set(true);
    const v = this.form.value;
    this.usersService.createStaff({
      fullName:    v.fullName!,
      email:       v.email!,
      password:    v.password!,
      role:        v.role as 'preceptor' | 'supervisor',
      institution: v.institution || undefined,
      phone:       v.phone || undefined
    }).subscribe({
      next: () => { this.busy.set(false); this.dialogRef.close(true); },
      error: (err) => { this.busy.set(false); console.error(err); }
    });
  }
}
