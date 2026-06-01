import { HttpErrorResponse } from '@angular/common/http';

/** RFC-7807 ValidationProblem body shape returned by the backend on a 400. */
interface ValidationProblemBody {
  errors?: Record<string, string[]>;
}

/**
 * Flattens a backend `400 ValidationProblem` body into a list of displayable messages.
 * The backend keys each entry by Identity error code (e.g. `DuplicateUserName`,
 * `PasswordTooShort`); we surface the human-readable descriptions. Returns a generic
 * fallback when the body isn't the expected RFC-7807 shape.
 */
export function mapValidationProblem(error: HttpErrorResponse): string[] {
  const body = error.error as ValidationProblemBody | null;
  const errors = body?.errors;
  if (!errors) {
    return ['Something went wrong. Please try again.'];
  }

  const messages = Object.values(errors).flat();
  return messages.length > 0 ? messages : ['Something went wrong. Please try again.'];
}
