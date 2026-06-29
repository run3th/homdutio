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

/** One requirement in the live password-rule checklist (register + reset-password UIs). */
export interface PasswordRule {
  label: string;
  met: boolean;
}

/**
 * The four password requirements shown live (with checkmarks) as the user types, mirroring the policy
 * {@link passwordPolicyValidator} enforces — the upper/lower classes are combined into one row to match
 * the mockup. Purely presentational; submission is still gated by the validator. Shared so the register
 * and reset-password forms render an identical checklist from one source.
 */
export function passwordRuleChecklist(value: string | null | undefined): PasswordRule[] {
  const v = value ?? '';
  return [
    { label: 'At least 6 characters', met: v.length >= 6 },
    { label: 'An uppercase and a lowercase letter', met: /[a-z]/.test(v) && /[A-Z]/.test(v) },
    { label: 'A digit', met: /[0-9]/.test(v) },
    { label: 'A non-alphanumeric character (e.g. ! ? #)', met: /[^a-zA-Z0-9]/.test(v) },
  ];
}
