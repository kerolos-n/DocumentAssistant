import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import env from '../../environments/environment';
import { routes } from '../app.routes';
import { AuthPage } from './auth-page';
import { authInterceptor } from './auth.interceptor';

describe('AuthPage', () => {
  let httpTesting: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      imports: [AuthPage],
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        provideRouter(routes),
      ],
    });
    httpTesting = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpTesting.verify();
    localStorage.clear();
  });

  function fill(root: HTMLElement, values: readonly (readonly [string, string])[]): void {
    for (const [selector, value] of values) {
      const input = root.querySelector(selector) as HTMLInputElement;
      input.value = value;
      input.dispatchEvent(new Event('input'));
    }
  }

  function submitButton(root: HTMLElement): HTMLButtonElement {
    return root.querySelector('button[type="submit"]') as HTMLButtonElement;
  }

  function switchToRegister(root: HTMLElement): void {
    const registerTab = Array.from(root.querySelectorAll('button')).find(
      (button) => button.textContent?.trim() === 'Register',
    );
    registerTab?.click();
  }

  it('disables submit until the login form has valid input', () => {
    const fixture = TestBed.createComponent(AuthPage);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;

    expect(submitButton(root).disabled).toBe(true);

    fill(root, [['#email', 'reader@example.com']]);
    fixture.detectChanges();
    expect(submitButton(root).disabled).toBe(true);

    fill(root, [['#password', 'Correct1!']]);
    fixture.detectChanges();
    expect(submitButton(root).disabled).toBe(false);
  });

  it('disables submit while the register passwords do not match', () => {
    const fixture = TestBed.createComponent(AuthPage);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;

    switchToRegister(root);
    fixture.detectChanges();

    fill(root, [
      ['#email', 'reader@example.com'],
      ['#password', 'Correct1!'],
      ['#confirm-password', 'Different1!'],
    ]);
    fixture.detectChanges();
    expect(submitButton(root).disabled).toBe(true);

    fill(root, [['#confirm-password', 'Correct1!']]);
    fixture.detectChanges();
    expect(submitButton(root).disabled).toBe(false);
  });

  it('registers an account and redirects to /my-documents', async () => {
    const fixture = TestBed.createComponent(AuthPage);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;

    switchToRegister(root);
    fixture.detectChanges();

    fill(root, [
      ['#email', 'reader@example.com'],
      ['#password', 'Correct1!'],
      ['#confirm-password', 'Correct1!'],
    ]);
    fixture.detectChanges();

    const form = root.querySelector('form') as HTMLFormElement;
    form.dispatchEvent(new Event('submit'));
    fixture.detectChanges();

    const registrationRequest = httpTesting.expectOne(`${env.API_URL}/api/auth/register`);
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
    await fixture.whenStable();

    expect(TestBed.inject(Router).url).toBe('/my-documents');
    // The /my-documents route loads the document list as soon as it activates.
    httpTesting
      .match(`${env.API_URL}/api/documents`)
      .forEach((request) => request.flush([]));
    expect(JSON.parse(localStorage.getItem('document-assistant.auth') ?? '{}')).toMatchObject({
      email: 'reader@example.com',
    });
  });
});
