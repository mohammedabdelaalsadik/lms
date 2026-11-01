import { HttpErrorResponse } from '@angular/common/http';
import { describe, expect, it } from 'vitest';
import { errorKey } from './api-error';

describe('errorKey', () => {
  it('maps a server error code to a translation key', () => {
    const error = new HttpErrorResponse({
      status: 409,
      error: { code: 'catalog.course.code_taken' },
    });

    expect(errorKey(error)).toBe('errors.catalog_course_code_taken');
  });

  it('recognizes network failures and rate limiting', () => {
    expect(errorKey(new HttpErrorResponse({ status: 0 }))).toBe('errors.network');
    expect(errorKey(new HttpErrorResponse({ status: 429 }))).toBe('errors.too_many_requests');
  });

  it('falls back when the code is missing or malformed', () => {
    const malformed = new HttpErrorResponse({ status: 500, error: { code: '<script>' } });

    expect(errorKey(malformed)).toBe('errors.common_unexpected');
    expect(errorKey(new Error('boom'))).toBe('errors.common_unexpected');
  });
});
