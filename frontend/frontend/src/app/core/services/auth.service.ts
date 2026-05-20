import { Injectable, signal, computed } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { tap } from 'rxjs/operators';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AuthResponse, LoginDto, RegisterDto } from '../models/models';

const TOKEN_KEY = 'ec_token';
const USER_KEY  = 'ec_user';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly api = `${environment.apiUrl}/auth`;

  private _user = signal<AuthResponse | null>(this.loadUser());
  readonly user    = this._user.asReadonly();
  readonly isAuth  = computed(() => !!this._user());
  readonly role    = computed(() => this._user()?.role ?? null);
  readonly userId  = computed(() => this._user()?.userId ?? null);

  constructor(private http: HttpClient, private router: Router) {}

  login(dto: LoginDto): Observable<AuthResponse> {
    return this.http.post<AuthResponse>(`${this.api}/login`, dto).pipe(
      tap(res => this.persist(res))
    );
  }

  register(dto: RegisterDto): Observable<AuthResponse> {
    return this.http.post<AuthResponse>(`${this.api}/register`, dto).pipe(
      tap(res => this.persist(res))
    );
  }

  /** Primeiro acesso: define e-mail acadêmico e nova senha */
  firstAccess(email: string, newPassword: string): Observable<AuthResponse> {
    return this.http.post<AuthResponse>(`${this.api}/first-access`, { email, newPassword }).pipe(
      tap(res => this.persist(res))
    );
  }

  /** Envia código de recuperação de senha para o e-mail @cs.udf.edu.br */
  forgotPassword(email: string): Observable<void> {
    return this.http.post<void>(`${this.api}/forgot-password`, { email });
  }

  /** Verifica se o código recebido por e-mail é válido */
  verifyResetCode(email: string, code: string): Observable<void> {
    return this.http.post<void>(`${this.api}/verify-reset-code`, { email, code });
  }

  /** Redefine a senha usando o código validado */
  resetPassword(email: string, code: string, newPassword: string): Observable<void> {
    return this.http.post<void>(`${this.api}/reset-password`, { email, code, newPassword });
  }

  logout(): void {
    localStorage.removeItem(TOKEN_KEY);
    localStorage.removeItem(USER_KEY);
    this._user.set(null);
    this.router.navigate(['/auth']);
  }

  getToken(): string | null {
    return localStorage.getItem(TOKEN_KEY);
  }

  private persist(res: AuthResponse): void {
    localStorage.setItem(TOKEN_KEY, res.token);
    localStorage.setItem(USER_KEY, JSON.stringify(res));
    this._user.set(res);
  }

  private loadUser(): AuthResponse | null {
    try {
      const raw = localStorage.getItem(USER_KEY);
      return raw ? JSON.parse(raw) : null;
    } catch {
      return null;
    }
  }
}
