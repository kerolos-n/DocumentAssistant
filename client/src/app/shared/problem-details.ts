/**
 * Pulls the first human-readable message out of an ASP.NET Core ProblemDetails body.
 * A validation `errors` dictionary wins over `detail`, which wins over `title`.
 * Returns `null` when the body carries nothing useful.
 */
export function flattenProblemDetails(body: unknown): string | null {
  if (typeof body !== 'object' || body === null) {
    return null;
  }

  const problem = body as Record<string, unknown>;

  const errors = problem['errors'];
  if (typeof errors === 'object' && errors !== null) {
    const messages = Object.values(errors).flatMap((value) =>
      Array.isArray(value) ? value.filter((item): item is string => typeof item === 'string') : [],
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

  return null;
}
