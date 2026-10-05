import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import env from '../../environments/environment';
import { AuthService } from './auth.service';
import { authInterceptor } from './auth.interceptor';

const TEST_API_URL = 'https://api.example.test';

describe('AuthService', () => {
  let authService: AuthService;
  let httpTesting: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [
        { provide: env.API_URL, useValue: TEST_API_URL },
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
      ],
    });

    authService = TestBed.inject(AuthService);
    httpTesting = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpTesting.verify();
    localStorage.clear();
  });

  it('stores a login token, sends it to protected API routes, and clears it on logout', () => {
    const session = {
      accessToken: 'signed-token',
      expiresAtUtc: new Date(Date.now() + 60_000).toISOString(),
      userId: 'user-123',
      email: 'reader@example.com',
    };

    authService.login(session.email, 'Correct1!').subscribe();
    httpTesting.expectOne(`${TEST_API_URL}/api/auth/login`).flush(session);

    expect(authService.getAccessToken()).toBe(session.accessToken);
    expect(JSON.parse(localStorage.getItem('document-assistant.auth') ?? '{}')).toEqual(session);

    authService.getCurrentUser().subscribe();
    const profileRequest = httpTesting.expectOne(`${TEST_API_URL}/api/me`);
    expect(profileRequest.request.headers.get('Authorization')).toBe('Bearer signed-token');
    profileRequest.flush({ userId: session.userId, email: session.email });

    authService.logout();
    expect(authService.getAccessToken()).toBeNull();
    expect(localStorage.getItem('document-assistant.auth')).toBeNull();

    authService.getCurrentUser().subscribe();
    const anonymousRequest = httpTesting.expectOne(`${TEST_API_URL}/api/me`);
    expect(anonymousRequest.request.headers.has('Authorization')).toBe(false);
    anonymousRequest.flush({ userId: session.userId, email: session.email });
  });
});
