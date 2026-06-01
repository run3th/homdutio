import { HttpErrorResponse } from '@angular/common/http';

import { mapValidationProblem } from './validation-problem';

describe('mapValidationProblem', () => {
  it('flattens an RFC-7807 errors map into a list of messages', () => {
    const error = new HttpErrorResponse({
      status: 400,
      error: {
        errors: {
          DuplicateUserName: ['Email is already taken.'],
          PasswordTooShort: ['Password is too short.'],
        },
      },
    });

    expect(mapValidationProblem(error)).toEqual([
      'Email is already taken.',
      'Password is too short.',
    ]);
  });

  it('returns a generic message when the body has no errors map', () => {
    const error = new HttpErrorResponse({ status: 400, error: {} });
    expect(mapValidationProblem(error)).toEqual(['Something went wrong. Please try again.']);
  });
});
