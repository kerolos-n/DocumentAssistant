import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

/** Sends signed-out visitors to the auth page. */
export const authGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);

  return auth.session() ? true : router.createUrlTree(['/auth']);
};

/** Keeps signed-in users out of the auth page. */
export const guestGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);

  return auth.session() ? router.createUrlTree(['/my-documents']) : true;
};

/** Public home: guests stay here, signed-in users are sent to their documents. */
export const homeGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);

  return auth.session() ? router.createUrlTree(['/my-documents']) : true;
};
