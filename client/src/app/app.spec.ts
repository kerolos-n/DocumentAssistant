import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { App } from './app';
import { routes } from './app.routes';

describe('App', () => {
  beforeEach(async () => {
    localStorage.clear();
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter(routes)],
    }).compileComponents();
  });

  it('should create the app', () => {
    const fixture = TestBed.createComponent(App);
    const app = fixture.componentInstance;
    expect(app).toBeTruthy();
  });

  it('should render title', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('h1')?.textContent).toContain('Document Assistant');
  });

  it('offers login and register links to signed-out visitors', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;

    // Scope to the account nav: the public home page also carries call-to-action links.
    const links = Array.from(root.querySelectorAll('nav[aria-label="Account"] a'));
    expect(links.map((link) => link.textContent?.trim())).toEqual(['Login', 'Register']);
    expect(links[0].getAttribute('href')).toBe('/auth');
    expect(links[1].getAttribute('href')).toContain('mode=register');
  });

  it('toggles the mobile navigation from the hamburger button', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;

    expect(root.querySelector('#mobile-nav')).toBeNull();

    const toggle = root.querySelector(
      'button[aria-label="Toggle navigation"]',
    ) as HTMLButtonElement;
    expect(toggle.getAttribute('aria-expanded')).toBe('false');

    toggle.click();
    fixture.detectChanges();

    const mobileNav = root.querySelector('#mobile-nav');
    expect(mobileNav).not.toBeNull();
    expect(toggle.getAttribute('aria-expanded')).toBe('true');
    expect(Array.from(mobileNav!.querySelectorAll('a')).map((link) => link.textContent?.trim())).toEqual(
      ['Login', 'Register'],
    );
  });

  it('clears the session and redirects home when signing out', async () => {
    localStorage.setItem(
      'document-assistant.auth',
      JSON.stringify({
        accessToken: 'signed-token',
        expiresAtUtc: new Date(Date.now() + 60_000).toISOString(),
        userId: 'user-1',
        email: 'reader@example.com',
      }),
    );

    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    await fixture.whenStable();

    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigate');
    const signOut = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll('button'),
    ).find((button) => button.textContent?.trim() === 'Sign out');
    signOut?.click();
    await fixture.whenStable();

    expect(localStorage.getItem('document-assistant.auth')).toBeNull();
    expect(navigate).toHaveBeenCalledWith(['/']);
  });
});
