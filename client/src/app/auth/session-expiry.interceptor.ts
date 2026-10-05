import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import env from '../../environments/environment';
import { AuthService } from './auth.service';

/** Auth endpoints answer 401 for bad credentials, which must not be read as a lost session. */
function isAuthEndpoint(url: string): boolean {
  return url.startsWith(`${env.API_URL}/api/auth/`);
}

/**
 * Global 401 handling: a token the API rejects means the stored session is stale, so drop it and
 * send the user to sign in rather than letting each page render its own "unauthorized" state.
 * The failed request still reaches the caller, which can show a message if it needs to.
 */
export const sessionExpiryInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  return next(request).pipe(
    catchError((error: unknown) => {
      if (
        error instanceof HttpErrorResponse &&
        error.status === 401 &&
        !isAuthEndpoint(request.url)
      ) {
        auth.logout();
        void router.navigate(['/auth']);
      }

      return throwError(() => error);
    }),
  );
};
