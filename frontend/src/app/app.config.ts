import { provideHttpClient, withInterceptors } from '@angular/common/http';
import {
  ApplicationConfig,
  inject,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { DateAdapter, provideNativeDateAdapter } from '@angular/material/core';
import { MAT_FORM_FIELD_DEFAULT_OPTIONS } from '@angular/material/form-field';
import { MAT_ICON_DEFAULT_OPTIONS } from '@angular/material/icon';
import { MatPaginatorIntl } from '@angular/material/paginator';
import { TitleStrategy, provideRouter, withComponentInputBinding } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { provideTranslateHttpLoader } from '@ngx-translate/http-loader';
import { routes } from './app.routes';
import { AuthService } from './core/auth/auth.service';
import { apiInterceptor } from './core/http/api.interceptor';
import { LanguageService } from './core/i18n/language.service';
import { LocalizedTitleStrategy } from './core/i18n/localized-title';
import { LocalizedPaginatorIntl } from './core/i18n/paginator-intl';
import { ReadableDateAdapter } from './core/i18n/readable-date-adapter';
import { ThemeService } from './core/theme/theme.service';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes, withComponentInputBinding()),
    provideHttpClient(withInterceptors([apiInterceptor])),
    provideNativeDateAdapter({
      parse: { dateInput: null },
      display: {
        dateInput: { year: 'numeric', month: 'short', day: 'numeric' },
        monthYearLabel: { year: 'numeric', month: 'long' },
        dateA11yLabel: { year: 'numeric', month: 'long', day: 'numeric' },
        monthYearA11yLabel: { year: 'numeric', month: 'long' },
      },
    }),
    { provide: DateAdapter, useClass: ReadableDateAdapter },
    { provide: TitleStrategy, useClass: LocalizedTitleStrategy },
    provideTranslateService({
      fallbackLang: 'en',
      loader: provideTranslateHttpLoader({ prefix: '/i18n/', suffix: '.json' }),
    }),
    provideAppInitializer(async () => {
      const language = inject(LanguageService);
      const auth = inject(AuthService);
      inject(ThemeService).init();
      await language.init();
      await auth.restore();
    }),
    { provide: MatPaginatorIntl, useClass: LocalizedPaginatorIntl },
    { provide: MAT_ICON_DEFAULT_OPTIONS, useValue: { fontSet: 'material-symbols-outlined' } },
    {
      provide: MAT_FORM_FIELD_DEFAULT_OPTIONS,
      useValue: { appearance: 'outline', subscriptSizing: 'dynamic' },
    },
  ],
};
