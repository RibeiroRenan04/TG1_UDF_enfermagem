import { Injectable, signal, computed } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { tap } from 'rxjs/operators';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AuthResponse, LoginDto, ResponsibilityTerms } from '../models/models';

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

  /**
   * A coordenadora (secretaria/estagiária) enxerga o mesmo que o professor, porém
   * sem qualquer permissão de alteração. Use este sinal para esconder ou desabilitar
   * ações de escrita — o backend também bloqueia, isto é apenas a camada visual.
   */
  readonly somenteLeitura = computed(() => this._user()?.role === 'coordenadora');

  /** Professor responsável: único perfil com acesso total de gestão. */
  readonly ehProfessor = computed(() => this._user()?.role === 'supervisor');

  /** Professor ou coordenadora: quem enxerga os painéis de gestão. */
  readonly ehGestao = computed(() =>
    this._user()?.role === 'supervisor' || this._user()?.role === 'coordenadora');

  /** Perfis que precisam aceitar o termo de responsabilidade de acesso. */
  readonly deveAceitarTermo = computed(() => this._user()?.mustAcceptTerms === true);

  constructor(private http: HttpClient, private router: Router) {}

  login(dto: LoginDto): Observable<AuthResponse> {
    return this.http.post<AuthResponse>(`${this.api}/login`, dto).pipe(
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

  /** Texto do termo de responsabilidade (mantido no backend para versionamento). */
  getTerms(): Observable<ResponsibilityTerms> {
    return this.http.get<ResponsibilityTerms>(`${this.api}/terms`);
  }

  /** Registra o aceite do termo e libera o acesso do usuário autenticado. */
  acceptTerms(): Observable<{ acceptedAt: string; versao: string }> {
    return this.http.post<{ acceptedAt: string; versao: string }>(
      `${this.api}/accept-terms`, { accepted: true }
    ).pipe(
      tap(() => {
        const atual = this._user();
        if (atual) this.persist({ ...atual, mustAcceptTerms: false });
      })
    );
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
