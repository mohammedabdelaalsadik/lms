import { Pipe, PipeTransform, inject } from '@angular/core';
import { LanguageService } from '../core/i18n/language.service';

/** Arabic shows the Umm al-Qura Hijri date, English the Gregorian one. */
@Pipe({ name: 'appDate', pure: false })
export class AppDatePipe implements PipeTransform {
  private readonly languages = inject(LanguageService);

  transform(value: string | null | undefined, withTime = false): string {
    if (!value) {
      return '';
    }

    const locale =
      this.languages.language() === 'ar' ? 'ar-SA-u-ca-islamic-umalqura-nu-latn' : 'en-GB';

    return new Intl.DateTimeFormat(locale, { dateStyle: 'medium', timeStyle: withTime ? 'short' : undefined }).format(new Date(value));
  }
}
