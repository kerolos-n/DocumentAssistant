import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import env from '../../environments/environment';
import { AuthService } from './auth.service';

export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const token = inject(AuthService).getAccessToken();
  if (!token || !request.url.startsWith(`${env.API_URL}/api/`)) {
    return next(request);
  }

  return next(request.clone({ setHeaders: { Authorization: `Bearer ${token}` } }));
};
