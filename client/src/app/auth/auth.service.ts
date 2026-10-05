import { HttpClient } from '@angular/common/http';
import { inject, Injectable, signal } from '@angular/core';
import { tap } from 'rxjs';
import env from '../../environments/environment';

export interface AuthSession {
  readonly accessToken: string;
  readonly expiresAtUtc: string;
  readonly userId: string;
  readonly email: string;
}

export interface CurrentUser {
  readonly userId: string;
  readonly email: string | null;
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly sessionKey = 'document-assistant.auth';
  private readonly sessionState = signal<AuthSession | null>(this.restoreSession());

  readonly session = this.sessionState.asReadonly();
  getAccessToken(): string | null {
    const session = this.sessionState();
    if (session && Date.parse(session.expiresAtUtc) > Date.now()) {
      return session.accessToken;
    }

    if (session) {
      this.logout();
    }
    return null;
  }

  register(email: string, password: string) {
    return this.http
      .post<AuthSession>(`${env.API_URL}/api/auth/register`, { email, password })
      .pipe(tap((session) => this.saveSession(session)));
  }

  login(email: string, password: string) {
    return this.http
      .post<AuthSession>(`${env.API_URL}/api/auth/login`, { email, password })
      .pipe(tap((session) => this.saveSession(session)));
  }

  getCurrentUser() {
    return this.http.get<CurrentUser>(`${env.API_URL}/api/me`);
  }

  logout(): void {
    localStorage.removeItem(this.sessionKey);
    this.sessionState.set(null);
  }

  private restoreSession(): AuthSession | null {
    const stored = localStorage.getItem(this.sessionKey);
    if (stored === null) {
      return null;
    }

    let parsed: unknown;
    try {
      parsed = JSON.parse(stored);
    } catch (error) {
      if (!(error instanceof SyntaxError)) {
        throw error;
      }
      localStorage.removeItem(this.sessionKey);
      return null;
    }

    if (this.isValidSession(parsed) && Date.parse(parsed.expiresAtUtc) > Date.now()) {
      return parsed;
    }

    localStorage.removeItem(this.sessionKey);
    return null;
  }

  private saveSession(value: AuthSession): void {
    if (!this.isValidSession(value) || Date.parse(value.expiresAtUtc) <= Date.now()) {
      throw new Error('The authentication server returned an invalid or expired token.');
    }

    localStorage.setItem(this.sessionKey, JSON.stringify(value));
    this.sessionState.set(value);
  }

  private isValidSession(value: unknown): value is AuthSession {
    if (typeof value !== 'object' || value === null) {
      return false;
    }

    const session = value as Record<string, unknown>;
    return (
      typeof session['accessToken'] === 'string' &&
      typeof session['expiresAtUtc'] === 'string' &&
      Number.isFinite(Date.parse(session['expiresAtUtc'])) &&
      typeof session['userId'] === 'string' &&
      typeof session['email'] === 'string'
    );
  }
}
