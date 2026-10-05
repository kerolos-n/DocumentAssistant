import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  AbstractControl,
  FormControl,
  FormGroup,
  ReactiveFormsModule,
  ValidationErrors,
  ValidatorFn,
  Validators,
} from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { finalize } from 'rxjs';
import { flattenProblemDetails } from '../shared/problem-details';
import { AuthService } from './auth.service';

type AuthMode = 'login' | 'register';

/**
 * Register mode only. An empty confirmation is left to the control's own
 * `Validators.required`, so login mode — where the field is hidden — stays valid.
 */
const passwordsMatch: ValidatorFn = (form: AbstractControl): ValidationErrors | null => {
  const password = form.get('password')?.value as string | undefined;
  const confirmation = form.get('confirmPassword')?.value as string | undefined;
  if (!confirmation) {
    return null;
  }
  return password === confirmation ? null : { passwordsMismatch: true };
};

@Component({
  imports: [ReactiveFormsModule],
  selector: 'app-auth-page',
  templateUrl: './auth-page.html',
})
export class AuthPage {
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  protected readonly mode = signal<AuthMode>('login');
  protected readonly isSubmitting = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly isRegistering = computed(() => this.mode() === 'register');

  protected readonly form = new FormGroup(
    {
      email: new FormControl('', {
        nonNullable: true,
        validators: [Validators.required, Validators.email],
      }),
      password: new FormControl('', {
        nonNullable: true,
        validators: [Validators.required, Validators.minLength(8)],
      }),
      confirmPassword: new FormControl('', { nonNullable: true }),
    },
    { validators: [passwordsMatch] },
  );

  constructor() {
    // The nav bar links here with ?mode=register to open straight on the register form.
    this.route.queryParamMap.pipe(takeUntilDestroyed()).subscribe((params) => {
      this.setMode(params.get('mode') === 'register' ? 'register' : 'login');
    });
  }

  protected setMode(mode: AuthMode): void {
    this.mode.set(mode);
    this.errorMessage.set(null);
    // Confirm is only required while registering; reset it so a value left over
    // from the other mode cannot keep the form invalid.
    this.form.controls.confirmPassword.setValidators(
      mode === 'register' ? [Validators.required] : null,
    );
    this.form.controls.confirmPassword.reset('');
  }

  protected submit(): void {
    this.errorMessage.set(null);

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const { email, password } = this.form.getRawValue();

    const request = this.isRegistering()
      ? this.authService.register(email, password)
      : this.authService.login(email, password);

    this.isSubmitting.set(true);
    request.pipe(finalize(() => this.isSubmitting.set(false))).subscribe({
      next: () => {
        void this.router.navigate(['/ask']);
      },
      error: (error: unknown) => {
        this.errorMessage.set(this.getErrorMessage(error));
      },
    });
  }

  private getErrorMessage(error: unknown): string {
    if (error instanceof HttpErrorResponse) {
      if (error.status === 0) {
        return 'Could not reach the server. Check that the API is running.';
      }

      const message = flattenProblemDetails(error.error);
      if (message) {
        return message;
      }

      if (error.status === 401) {
        return 'Email or password is incorrect.';
      }
    }

    return 'Authentication failed. Please try again.';
  }
}
