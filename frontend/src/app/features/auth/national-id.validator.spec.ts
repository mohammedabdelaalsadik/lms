import { FormControl } from '@angular/forms';
import { describe, expect, it } from 'vitest';
import { nationalIdValidator } from './national-id.validator';

const check = (value: string) => nationalIdValidator(new FormControl(value, { nonNullable: true }));

describe('nationalIdValidator', () => {
  it.each(['1000000008', '2000000006'])('accepts %s, matching the server rule', (value) => {
    expect(check(value)).toBeNull();
  });

  it.each(['123', '3000000004', '1000000009', '10000000a8'])('rejects %s', (value) => {
    expect(check(value)).toEqual({ nationalId: true });
  });

  it('leaves empty values to the required validator', () => {
    expect(check('')).toBeNull();
  });
});
