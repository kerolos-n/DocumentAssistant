import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { UrlTree, provideRouter } from '@angular/router';
import { authGuard, guestGuard, homeGuard } from './auth.guards';

const SESSION_KEY = 'document-assistant.auth';

function configure(): void {
  TestBed.configureTestingModule({
    providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
  });
}

function signIn(): void {
  localStorage.setItem(
    SESSION_KEY,
    JSON.stringify({
      accessToken: 'token',
      expiresAtUtc: new Date(Date.now() + 60_000).toISOString(),
      userId: 'user-1',
      email: 'reader@example.com',
    }),
  );
}

describe('auth guards', () => {
  beforeEach(() => localStorage.clear());

  afterEach(() => localStorage.clear());

  it('authGuard sends signed-out visitors to /auth', () => {
    configure();
    const result = TestBed.runInInjectionContext(() => authGuard({} as never, {} as never));
    expect(result).toBeInstanceOf(UrlTree);
    expect((result as UrlTree).toString()).toBe('/auth');
  });

  it('authGuard lets signed-in users through', () => {
    signIn();
    configure();
    const result = TestBed.runInInjectionContext(() => authGuard({} as never, {} as never));
    expect(result).toBe(true);
  });

  it('guestGuard sends signed-in users to /my-documents', () => {
    signIn();
    configure();
    const result = TestBed.runInInjectionContext(() => guestGuard({} as never, {} as never));
    expect(result).toBeInstanceOf(UrlTree);
    expect((result as UrlTree).toString()).toBe('/my-documents');
  });

  it('guestGuard lets signed-out visitors through', () => {
    configure();
    const result = TestBed.runInInjectionContext(() => guestGuard({} as never, {} as never));
    expect(result).toBe(true);
  });

  it('homeGuard leaves signed-out visitors on the home page', () => {
    configure();
    const result = TestBed.runInInjectionContext(() => homeGuard({} as never, {} as never));
    expect(result).toBe(true);
  });

  it('homeGuard sends signed-in users to /my-documents', () => {
    signIn();
    configure();
    const result = TestBed.runInInjectionContext(() => homeGuard({} as never, {} as never));
    expect(result).toBeInstanceOf(UrlTree);
    expect((result as UrlTree).toString()).toBe('/my-documents');
  });
});
