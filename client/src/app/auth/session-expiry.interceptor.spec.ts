import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import env from '../../environments/environment';
import { routes } from '../app.routes';
import { AuthService } from './auth.service';
import { sessionExpiryInterceptor } from './session-expiry.interceptor';

describe('sessionExpiryInterceptor', () => {
  let httpTesting: HttpTestingController;
  let auth: AuthService;
  let router: Router;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([sessionExpiryInterceptor])),
        provideHttpClientTesting(),
        provideRouter(routes),
      ],
    });

    httpTesting = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
    router = TestBed.inject(Router);
  });

  afterEach(() => {
    httpTesting.verify();
    localStorage.clear();
  });

  function signIn(): void {
    auth.login('reader@example.com', 'Correct1!').subscribe();
    httpTesting.expectOne(`${env.API_URL}/api/auth/login`).flush({
      accessToken: 'stale-token',
      expiresAtUtc: new Date(Date.now() + 60_000).toISOString(),
      userId: 'user-1',
      email: 'reader@example.com',
    });
  }

  it('clears the session and redirects to sign-in when a protected call is rejected', async () => {
    signIn();
    expect(auth.getAccessToken()).toBe('stale-token');

    const navigate = vi.spyOn(router, 'navigate');
    auth.getCurrentUser().subscribe({ error: () => undefined });

    httpTesting
      .expectOne(`${env.API_URL}/api/me`)
      .flush(null, { status: 401, statusText: 'Unauthorized' });
    await Promise.resolve();

    expect(auth.getAccessToken()).toBeNull();
    expect(localStorage.getItem('document-assistant.auth')).toBeNull();
    expect(navigate).toHaveBeenCalledWith(['/auth']);
  });

  it('does not treat a rejected login as an expired session', () => {
    const navigate = vi.spyOn(router, 'navigate');
    auth.login('reader@example.com', 'wrong').subscribe({ error: () => undefined });

    httpTesting
      .expectOne(`${env.API_URL}/api/auth/login`)
      .flush(null, { status: 401, statusText: 'Unauthorized' });

    expect(navigate).not.toHaveBeenCalled();
  });
});
