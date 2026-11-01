import { HttpErrorResponse } from '@angular/common/http';
import { Component, effect, inject, input, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { Router, RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';
import { errorKey } from '../../core/http/api-error';
import { AppDatePipe } from '../../shared/app-date.pipe';
import { LocalizedPipe } from '../../shared/localized.pipe';
import { AuthLayout } from '../auth/auth-layout';
import { CertificateService, CertificateVerification } from './certificate.service';

/** Public page, reached by typing a code or scanning a certificate's QR code. */
@Component({
  selector: 'app-verify',
  imports: [
    AppDatePipe,
    AuthLayout,
    LocalizedPipe,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    ReactiveFormsModule,
    RouterLink,
    TranslatePipe,
  ],
  templateUrl: './verify.html',
  styles: `
    .box {
      display: grid;
      gap: var(--space-4);
      width: 100%;
      max-width: 26rem;
    }
    h1 {
      font: var(--mat-sys-headline-small);
      font-weight: 600;
    }
    header p {
      margin-block-start: var(--space-1);
      color: var(--mat-sys-on-surface-variant);
    }
    form {
      display: grid;
      gap: var(--space-3);
    }
    .result {
      display: grid;
      gap: var(--space-3);
      padding: var(--space-4);
      border: 1px solid var(--line);
      border-radius: var(--radius-m);
    }
    .verdict {
      display: flex;
      align-items: center;
      gap: var(--space-2);
      font: var(--mat-sys-title-medium);
      font-weight: 600;
      color: var(--mat-sys-primary);
    }
    .verdict.bad {
      color: var(--mat-sys-error);
    }
    dl {
      display: grid;
      gap: var(--space-2);
      margin: 0;
    }
    dt {
      color: var(--mat-sys-on-surface-variant);
    }
    dd {
      margin: 0;
      font-weight: 600;
    }
    .back {
      text-align: center;
    }
  `,
})
export class Verify {
  private readonly certificates = inject(CertificateService);
  private readonly router = inject(Router);

  readonly code = input<string>();

  protected readonly field = new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.pattern(/\S/)] });
  protected readonly busy = signal(false);
  protected readonly result = signal<CertificateVerification | null>(null);
  protected readonly notFound = signal(false);
  protected readonly error = signal<string | null>(null);

  constructor() {
    effect(() => {
      const code = this.code();

      if (code) {
        this.field.setValue(code);
        void this.check(code);
      }
    });
  }

  protected submit(event: Event): void {
    event.preventDefault();

    if (this.field.invalid) {
      this.field.markAsTouched();
      return;
    }

    void this.router.navigate(['/verify', this.field.value.trim()]);
  }

  private async check(code: string): Promise<void> {
    this.busy.set(true);
    this.result.set(null);
    this.notFound.set(false);
    this.error.set(null);

    try {
      this.result.set(await firstValueFrom(this.certificates.verify(code)));
    } catch (error) {
      if (error instanceof HttpErrorResponse && error.status === 404) {
        this.notFound.set(true);
      } else {
        this.error.set(errorKey(error));
      }
    } finally {
      this.busy.set(false);
    }
  }
}
