import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { Role } from '../models';
import { AuthService } from './auth.service';

export const authGuard: CanActivateFn = () =>
  inject(AuthService).isAuthenticated() || inject(Router).createUrlTree(['/login']);

/** Staff who owe a second factor go straight to setting it up. The API refuses everything else anyway. */
export const mfaSetupGuard: CanActivateFn = (_route, state) => {
  const user = inject(AuthService).user();
  return !(user?.mfaSetupRequired || user?.mustChangePassword) || state.url.startsWith('/security') || inject(Router).createUrlTree(['/security']);
};

export const guestGuard: CanActivateFn = () =>
  !inject(AuthService).isAuthenticated() || inject(Router).createUrlTree(['/dashboard']);

/** Hides screens only. The API enforces every permission on its own. */
export const roleGuard =
  (...roles: Role[]): CanActivateFn =>
  () =>
    inject(AuthService).hasRole(...roles) || inject(Router).createUrlTree(['/dashboard']);
