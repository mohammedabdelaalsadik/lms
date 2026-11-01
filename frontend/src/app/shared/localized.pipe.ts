import { Pipe, PipeTransform, inject } from '@angular/core';
import { LanguageService } from '../core/i18n/language.service';
import { LocalizedText } from '../core/models';

@Pipe({ name: 'localized', pure: false })
export class LocalizedPipe implements PipeTransform {
  private readonly languages = inject(LanguageService);

  transform(value: LocalizedText | null | undefined): string {
    return value?.[this.languages.language()] ?? '';
  }
}
