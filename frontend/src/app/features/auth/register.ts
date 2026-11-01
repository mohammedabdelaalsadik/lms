import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { Router, RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { AuthService } from '../../core/auth/auth.service';
import { errorKey } from '../../core/http/api-error';
import { LanguageService } from '../../core/i18n/language.service';
import { AuthLayout } from './auth-layout';
import { nationalIdValidator } from './national-id.validator';

@Component({
  selector: 'app-register',
  imports: [
    AuthLayout,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    ReactiveFormsModule,
    RouterLink,
    TranslatePipe,
  ],
  templateUrl: './register.html',
  styleUrl: './auth-form.scss',
})
export class Register {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly languages = inject(LanguageService);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    nationalId: ['', [Validators.required, nationalIdValidator]],
    fullNameAr: ['', [Validators.required, Validators.maxLength(150)]],
    fullNameEn: ['', [Validators.required, Validators.maxLength(150)]],
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required, Validators.minLength(10), Validators.maxLength(128)]],
  });
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected async submit(): Promise<void> {
    if (this.form.invalid || this.busy()) {
      this.form.markAllAsTouched();
      return;
    }

    this.busy.set(true);
    this.error.set(null);

    try {
      const value = this.form.getRawValue();
      await this.auth.register({
        ...value,
        nationalId: value.nationalId.trim(),
        language: this.languages.language() === 'ar' ? 'Ar' : 'En',
      });
      await this.auth.login(value.nationalId.trim(), value.password);
      await this.router.navigateByUrl('/dashboard');
    } catch (error) {
      this.error.set(errorKey(error));
    } finally {
      this.busy.set(false);
    }
  }
}
