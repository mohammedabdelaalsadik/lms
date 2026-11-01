import { Direction, Directionality } from '@angular/cdk/bidi';
import { DOCUMENT, Injectable, computed, inject, signal } from '@angular/core';
import { DateAdapter } from '@angular/material/core';
import { TranslateService } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';

export type AppLanguage = 'ar' | 'en';

const STORAGE_KEY = 'lms.language';

function readStored(): AppLanguage {
  try {
    return localStorage.getItem(STORAGE_KEY) === 'en' ? 'en' : 'ar';
  } catch {
    return 'ar';
  }
}

@Injectable({ providedIn: 'root' })
export class LanguageService {
  private readonly translate = inject(TranslateService);
  private readonly directionality = inject(Directionality);
  private readonly document = inject(DOCUMENT);
  private readonly dates = inject(DateAdapter);

  readonly language = signal<AppLanguage>(readStored());
  readonly direction = computed<Direction>(() => (this.language() === 'ar' ? 'rtl' : 'ltr'));

  init(): Promise<void> {
    return this.use(this.language());
  }

  async use(language: AppLanguage): Promise<void> {
    await firstValueFrom(this.translate.use(language));
    this.language.set(language);

    const direction = this.direction();
    this.document.documentElement.lang = language;
    this.document.documentElement.dir = direction;

    // Overlays (menus, dialogs) read the root Directionality rather than the DOM attribute.
    this.directionality.valueSignal.set(direction);
    this.directionality.change.emit(direction);
    this.dates.setLocale(language === 'ar' ? 'ar-SA-u-ca-gregory-nu-latn' : 'en-GB');

    try {
      localStorage.setItem(STORAGE_KEY, language);
    } catch {
      // Without storage the choice simply lasts for this session.
    }
  }
}
