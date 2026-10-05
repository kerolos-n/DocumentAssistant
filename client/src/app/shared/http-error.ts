import { HttpErrorResponse } from '@angular/common/http';
import { flattenProblemDetails } from './problem-details';

/**
 * Status-specific messages for the failure modes the API can return. Centralised so every page
 * reacts to 401/403/404/429/500 the same way instead of each inventing its own wording.
 */
export const OFFLINE_MESSAGE = 'Could not reach the server. Check that the API is running.';
export const SESSION_EXPIRED_MESSAGE = 'Your session has expired. Please sign in again.';
export const FORBIDDEN_MESSAGE = 'You do not have permission to do that.';
export const NOT_FOUND_MESSAGE = 'We could not find what you were looking for.';
export const RATE_LIMITED_MESSAGE = 'Too many requests. Please try again in a moment.';
export const SERVER_ERROR_MESSAGE = 'Something went wrong on our end. Please try again.';
export const ASSISTANT_UNAVAILABLE_MESSAGE =
  'The assistant is temporarily unavailable. Please try again.';

/**
 * Turns an HTTP failure into a message the user can act on. A body the server wrote for this
 * status (ProblemDetails) wins when it carries something usable; otherwise the caller's fallback
 * applies. `error.status === 0` means the request never reached the API.
 */
export function httpErrorMessage(error: unknown, fallback: string): string {
  if (!(error instanceof HttpErrorResponse)) {
    return fallback;
  }

  switch (error.status) {
    case 0:
      return OFFLINE_MESSAGE;
    // The API answers 502/504 when the model provider fails or times out.
    case 502:
    case 504:
      return ASSISTANT_UNAVAILABLE_MESSAGE;
    case 401:
      return flattenProblemDetails(error.error) ?? SESSION_EXPIRED_MESSAGE;
    case 403:
      return flattenProblemDetails(error.error) ?? FORBIDDEN_MESSAGE;
    case 404:
      return flattenProblemDetails(error.error) ?? NOT_FOUND_MESSAGE;
    case 429:
      return flattenProblemDetails(error.error) ?? RATE_LIMITED_MESSAGE;
    case 500:
      return flattenProblemDetails(error.error) ?? SERVER_ERROR_MESSAGE;
    default:
      return flattenProblemDetails(error.error) ?? fallback;
  }
}
