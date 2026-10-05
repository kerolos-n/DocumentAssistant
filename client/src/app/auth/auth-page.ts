import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { finalize } from 'rxjs';
import { AuthService, CurrentUser } from './auth.service';

type AuthMode = 'login' | 'register';

@Component({
  imports: [ReactiveFormsModule],
  selector: 'app-auth-page',
  templateUrl: './auth-page.html',
})
export class AuthPage {
  private readonly authService = inject(AuthService);

  protected readonly authSession = this.authService.session;
  protected readonly mode = signal<AuthMode>('login');
  protected readonly isSubmitting = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly successMessage = signal<string | null>(null);
  protected readonly currentUser = signal<CurrentUser | null>(null);
  protected readonly isRegistering = computed(() => this.mode() === 'register');

  protected readonly form = new FormGroup({
    email: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.email],
    }),
    password: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.minLength(8)],
    }),
    confirmPassword: new FormControl('', { nonNullable: true }),
  });

  protected setMode(mode: AuthMode): void {
    this.mode.set(mode);
    this.errorMessage.set(null);
    this.successMessage.set(null);
    this.currentUser.set(null);
  }

  protected submit(): void {
    this.errorMessage.set(null);
    this.successMessage.set(null);
    this.currentUser.set(null);

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const { email, password, confirmPassword } = this.form.getRawValue();
    if (this.isRegistering() && password !== confirmPassword) {
      this.errorMessage.set('Passwords do not match.');
      return;
    }

    const request = this.isRegistering()
      ? this.authService.register(email, password)
      : this.authService.login(email, password);

    this.isSubmitting.set(true);
    request.pipe(finalize(() => this.isSubmitting.set(false))).subscribe({
      next: (session) => {
        this.successMessage.set(`Signed in as ${session.email}.`);
      },
      error: (error: unknown) => {
        this.errorMessage.set(this.getErrorMessage(error));
      },
    });
  }

  protected verifyProtectedAccess(): void {
    this.errorMessage.set(null);
    this.currentUser.set(null);
    this.isSubmitting.set(true);

    this.authService
      .getCurrentUser()
      .pipe(finalize(() => this.isSubmitting.set(false)))
      .subscribe({
        next: (user) => this.currentUser.set(user),
        error: (error: unknown) => this.errorMessage.set(this.getErrorMessage(error)),
      });
  }

  private getErrorMessage(error: unknown): string {
    if (error instanceof HttpErrorResponse) {
      if (error.status === 0) {
        return 'Could not reach the server. Check that the API is running.';
      }

      const body: unknown = error.error;
      if (typeof body === 'object' && body !== null) {
        const problem = body as Record<string, unknown>;
        const errors = problem['errors'];
        if (typeof errors === 'object' && errors !== null) {
          const messages = Object.values(errors).flatMap((value) =>
            Array.isArray(value)
              ? value.filter((item): item is string => typeof item === 'string')
              : [],
          );
          if (messages.length > 0) {
            return messages.join(' ');
          }
        }
        if (typeof problem['detail'] === 'string') {
          return problem['detail'];
        }
        if (typeof problem['title'] === 'string') {
          return problem['title'];
        }
      }

      if (error.status === 401) {
        return 'Email or password is incorrect.';
      }
    }

    return 'Authentication failed. Please try again.';
  }
}
