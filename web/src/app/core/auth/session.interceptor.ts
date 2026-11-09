import { HttpContextToken, HttpErrorResponse, type HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, from, switchMap, throwError } from 'rxjs';

import { problemCode } from '../http/problem';
import { AuthService } from './auth.service';

/** Endpoints whose 401 means "not signed in", not "session ended". */
const SESSION_PROBE_URLS = ['/api/access/me', '/api/access/login'];

const RETRIED = new HttpContextToken<boolean>(() => false);

/**
 * - 401 from the API while signed in: the session ended, so go to sign-in and come back afterwards.
 * - 400 invalid_antiforgery_token: the token went stale (e.g. after a long idle), so fetch a new one and retry once.
 */
export const sessionInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  return next(request).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse) || !request.url.startsWith('/api/')) {
        return throwError(() => error);
      }

      if (
        error.status === 401 &&
        auth.isAuthenticated() &&
        !SESSION_PROBE_URLS.some((url) => request.url.startsWith(url))
      ) {
        auth.sessionEnded();
        void router.navigate(['/login'], {
          queryParams: { returnUrl: router.url, reason: 'expired' },
        });
      }

      if (
        error.status === 400 &&
        problemCode(error) === 'invalid_antiforgery_token' &&
        !request.context.get(RETRIED)
      ) {
        return from(auth.refreshAntiforgeryToken()).pipe(
          switchMap(() => next(request.clone({ context: request.context.set(RETRIED, true) }))),
        );
      }

      return throwError(() => error);
    }),
  );
};
