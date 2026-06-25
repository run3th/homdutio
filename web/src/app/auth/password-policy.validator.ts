import { AbstractControl, ValidationErrors } from '@angular/forms';

/**
 * Mirrors Identity's default password policy (Program.cs): ≥6 chars and at least one
 * uppercase, lowercase, digit, and non-alphanumeric character. Keeps the client in sync
 * with the server so valid input never round-trips into a surprise 400. Shared by the
 * register and reset-password forms.
 */
export function passwordPolicyValidator(control: AbstractControl): ValidationErrors | null {
  const value = (control.value as string) ?? '';
  if (value.length === 0) {
    return null; // `required` owns the empty case.
  }

  const unmet: string[] = [];
  if (value.length < 6) unmet.push('at least 6 characters');
  if (!/[A-Z]/.test(value)) unmet.push('an uppercase letter');
  if (!/[a-z]/.test(value)) unmet.push('a lowercase letter');
  if (!/[0-9]/.test(value)) unmet.push('a digit');
  if (!/[^a-zA-Z0-9]/.test(value)) unmet.push('a non-alphanumeric character');

  return unmet.length > 0 ? { passwordPolicy: unmet } : null;
}
