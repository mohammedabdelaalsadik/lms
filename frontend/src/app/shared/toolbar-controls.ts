import { Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe } from '@ngx-translate/core';
import { AuthService } from '../core/auth/auth.service';
import { LanguageService } from '../core/i18n/language.service';
import { ThemeService } from '../core/theme/theme.service';

@Component({
  selector: 'app-toolbar-controls',
  imports: [MatButtonModule, MatIconModule, MatTooltipModule, TranslatePipe],
  template: `
    <button matButton (click)="toggleLanguage()" [attr.lang]="languages.language() === 'ar' ? 'en' : 'ar'">
      <mat-icon>translate</mat-icon>
      {{ languages.language() === 'ar' ? 'English' : 'العربية' }}
    </button>
    <button
      matIconButton
      (click)="theme.toggle()"
      [matTooltip]="'shell.toggleTheme' | translate"
      [attr.aria-label]="'shell.toggleTheme' | translate"
    >
      <mat-icon>{{ theme.theme() === 'dark' ? 'light_mode' : 'dark_mode' }}</mat-icon>
    </button>
  `,
  styles: ':host { display: inline-flex; align-items: center; gap: var(--space-1); }',
})
export class ToolbarControls {
  private readonly auth = inject(AuthService);
  protected readonly languages = inject(LanguageService);
  protected readonly theme = inject(ThemeService);

  protected toggleLanguage(): void {
    void this.auth.changeLanguage(this.languages.language() === 'ar' ? 'en' : 'ar');
  }
}
