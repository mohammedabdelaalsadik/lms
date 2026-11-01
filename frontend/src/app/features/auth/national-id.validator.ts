import { AbstractControl, ValidationErrors } from '@angular/forms';

/** Mirrors the server rule so people get instant feedback. The server remains the authority. */
export function nationalIdValidator(control: AbstractControl<string>): ValidationErrors | null {
  const value = control.value?.trim();

  if (!value) {
    return null;
  }

  if (!/^[12]\d{9}$/.test(value)) {
    return { nationalId: true };
  }

  const sum = [...value].reduce((total, char, index) => {
    const digit = Number(char);
    const weighted = index % 2 === 0 ? digit * 2 : digit;
    return total + (weighted > 9 ? weighted - 9 : weighted);
  }, 0);

  return sum % 10 === 0 ? null : { nationalId: true };
}
