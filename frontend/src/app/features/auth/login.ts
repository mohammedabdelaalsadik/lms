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
import { AuthLayout } from './auth-layout';

@Component({
  selector: 'app-login',
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
  templateUrl: './login.html',
  styleUrl: './auth-form.scss',
})
export class Login {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    nationalId: ['', Validators.required],
    password: ['', Validators.required],
  });
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly showPassword = signal(false);
  protected readonly challenge = signal<string | null>(null);
  protected readonly code = signal('');

  protected async verify(): Promise<void> {
    if (!this.code().trim() || this.busy()) {
      return;
    }

    this.busy.set(true);
    this.error.set(null);

    try {
      await this.auth.verifyMfa(this.challenge()!, this.code().trim());
      await this.router.navigateByUrl('/dashboard');
    } catch (error) {
      this.error.set(errorKey(error));
      this.code.set('');
    } finally {
      this.busy.set(false);
    }
  }

  protected back(): void {
    this.challenge.set(null);
    this.code.set('');
    this.error.set(null);
  }

  protected async submit(): Promise<void> {
    if (this.form.invalid || this.busy()) {
      this.form.markAllAsTouched();
      return;
    }

    this.busy.set(true);
    this.error.set(null);

    try {
      const { nationalId, password } = this.form.getRawValue();
      this.challenge.set(await this.auth.login(nationalId.trim(), password));

      if (!this.challenge()) {
        await this.router.navigateByUrl('/dashboard');
      }
    } catch (error) {
      this.error.set(errorKey(error));
    } finally {
      this.busy.set(false);
    }
  }
}
