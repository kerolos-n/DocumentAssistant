import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import env from '../../environments/environment';
import { AuthPage } from './auth-page';
import { authInterceptor } from './auth.interceptor';

const TEST_API_URL = 'https://api.example.test';

describe('AuthPage', () => {
  let httpTesting: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      imports: [AuthPage],
      providers: [
        { provide: env.API_URL, useValue: TEST_API_URL },
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    httpTesting = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpTesting.verify();
    localStorage.clear();
  });

  it('registers an account and can verify access to the protected endpoint', () => {
    const fixture = TestBed.createComponent(AuthPage);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;

    const registerTab = Array.from(root.querySelectorAll('button')).find(
      (button) => button.textContent?.trim() === 'Register',
    );
    registerTab?.click();
    fixture.detectChanges();

    const values = [
      ['#email', 'reader@example.com'],
      ['#password', 'Correct1!'],
      ['#confirm-password', 'Correct1!'],
    ] as const;
    for (const [selector, value] of values) {
      const input = root.querySelector(selector) as HTMLInputElement;
      input.value = value;
      input.dispatchEvent(new Event('input'));
    }
    fixture.detectChanges();

    const form = root.querySelector('form') as HTMLFormElement;
    form.dispatchEvent(new Event('submit'));
    fixture.detectChanges();

    const registrationRequest = httpTesting.expectOne(`${TEST_API_URL}/api/auth/register`);
    expect(registrationRequest.request.body).toEqual({
      email: 'reader@example.com',
      password: 'Correct1!',
    });
    registrationRequest.flush({
      accessToken: 'signed-token',
      expiresAtUtc: new Date(Date.now() + 60_000).toISOString(),
      userId: 'user-123',
      email: 'reader@example.com',
    });
    fixture.detectChanges();

    const verifyButton = Array.from(root.querySelectorAll('button')).find((button) =>
      button.textContent?.includes('Verify protected API access'),
    );
    verifyButton?.click();

    const profileRequest = httpTesting.expectOne(`${TEST_API_URL}/api/me`);
    expect(profileRequest.request.headers.get('Authorization')).toBe('Bearer signed-token');
    profileRequest.flush({ userId: 'user-123', email: 'reader@example.com' });
    fixture.detectChanges();

    expect(root.textContent).toContain('Authenticated as reader@example.com.');
  });
});
