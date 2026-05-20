import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../services/auth.service';

export const authGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  if (!auth.isAuth()) return router.createUrlTree(['/auth']);
  const u = auth.user();
  if (u?.mustChangePassword || u?.mustSetEmail) return router.createUrlTree(['/primeiro-acesso']);
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
