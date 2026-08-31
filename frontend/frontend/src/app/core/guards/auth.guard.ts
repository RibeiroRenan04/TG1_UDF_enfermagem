import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../services/auth.service';

export const authGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  if (!auth.isAuth()) return router.createUrlTree(['/auth']);
  const u = auth.user();
  if (u?.mustChangePassword || u?.mustSetEmail) return router.createUrlTree(['/primeiro-acesso']);
  // Preceptor, professor e coordenadora só entram após aceitar o termo de
  // responsabilidade de acesso (senha pessoal e intransferível).
  if (u?.mustAcceptTerms) return router.createUrlTree(['/termo-responsabilidade']);
  return true;
};

export const guestGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  if (!auth.isAuth()) return true;
  return router.createUrlTree(['/app']);
};

export const firstAccessGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  if (!auth.isAuth()) return router.createUrlTree(['/auth']);
  const u = auth.user();
  if (u?.mustChangePassword || u?.mustSetEmail) return true;
  return router.createUrlTree(['/app']);
};

/** Libera a tela do termo apenas para quem ainda precisa aceitá-lo. */
export const termsGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  if (!auth.isAuth()) return router.createUrlTree(['/auth']);
  const u = auth.user();
  if (u?.mustChangePassword || u?.mustSetEmail) return router.createUrlTree(['/primeiro-acesso']);
  if (u?.mustAcceptTerms) return true;
  return router.createUrlTree(['/app']);
};
