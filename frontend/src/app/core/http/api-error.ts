import { HttpErrorResponse } from '@angular/common/http';

/** Translation key for a failed call. Server error codes such as `a.b_c` map to `errors.a_b_c`. */
export function errorKey(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    if (error.status === 0) {
      return 'errors.network';
    }

    if (error.status === 429) {
      return 'errors.too_many_requests';
    }

    const code: unknown = error.error?.code;
    if (typeof code === 'string' && /^[a-z_.]+$/.test(code)) {
      return `errors.${code.replaceAll('.', '_')}`;
    }
  }

  return 'errors.common_unexpected';
}
