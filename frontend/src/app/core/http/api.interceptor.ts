import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, from, switchMap, throwError } from 'rxjs';
import { AuthService } from '../auth/auth.service';
import { LanguageService } from '../i18n/language.service';

/** Calls where a 401 is a real answer, so renewing the session would be wrong. */
const NO_RENEWAL = ['/auth/login', '/auth/password/forgot', '/auth/password/reset', '/auth/password', '/auth/mfa/verify', '/auth/register', '/auth/refresh', '/auth/logout'];

export const apiInterceptor: HttpInterceptorFn = (request, next) => {
  if (!request.url.startsWith('/api/')) {
    return next(request);
  }

  const language = inject(LanguageService);
  const auth = inject(AuthService);
  const router = inject(Router);

  const localized = request.clone({ setHeaders: { 'Accept-Language': language.language() } });

  return next(localized).pipe(
    catchError((error: unknown) => {
      const canRenew =
        error instanceof HttpErrorResponse &&
        error.status === 401 &&
        !NO_RENEWAL.some((path) => request.url.includes(path));

      if (!canRenew) {
        return throwError(() => error);
      }

      return from(auth.renewSession()).pipe(
        switchMap((renewed) => {
          if (renewed) {
            return next(localized);
          }

          const hadSession = auth.isAuthenticated();
          auth.clear();

          if (hadSession) {
            void router.navigateByUrl('/login');
          }

          return throwError(() => error);
        }),
      );
    }),
  );
};
