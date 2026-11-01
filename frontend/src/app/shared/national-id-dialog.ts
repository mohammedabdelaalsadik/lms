import { HttpClient } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { TranslatePipe } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';
import { errorKey } from '../core/http/api-error';

export interface NationalIdDialogData {
  userId: string;
  name: string;
}

/** Shows a full National ID after a written reason. It lives only in this dialog and leaves on close. */
@Component({
  selector: 'app-national-id-dialog',
  imports: [MatButtonModule, MatDialogModule, MatFormFieldModule, MatIconModule, MatInputModule, ReactiveFormsModule, TranslatePipe],
  template: `
    <h2 mat-dialog-title>{{ 'nationalId.title' | translate }}</h2>
    <mat-dialog-content>
      <p class="who">{{ data.name }}</p>

      @if (nationalId(); as value) {
        <p class="value ltr-value">{{ value }}</p>
        <p class="note"><mat-icon aria-hidden="true">history</mat-icon>{{ 'nationalId.logged' | translate }}</p>
      } @else {
        <form id="national-id-form" (submit)="submit($event)">
          <p class="note"><mat-icon aria-hidden="true">shield</mat-icon>{{ 'nationalId.warning' | translate }}</p>
          @if (error(); as key) {
            <div class="form-error" role="alert"><mat-icon aria-hidden="true">error</mat-icon>{{ key | translate }}</div>
          }
          <mat-form-field class="field">
            <mat-label>{{ 'nationalId.reason' | translate }}</mat-label>
            <textarea matInput rows="2" maxlength="500" [formControl]="reason"></textarea>
            <mat-error>{{ 'validation.required' | translate }}</mat-error>
          </mat-form-field>
        </form>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button matButton mat-dialog-close>{{ (nationalId() ? 'nationalId.close' : 'common.cancel') | translate }}</button>
      @if (!nationalId()) {
        <button matButton="filled" type="submit" form="national-id-form" [disabled]="busy()">{{ 'nationalId.show' | translate }}</button>
      }
    </mat-dialog-actions>
  `,
  styles: `
    form {
      display: grid;
      gap: var(--space-3);
    }
    .who {
      font-weight: 600;
    }
    .field {
      width: 100%;
    }
    .note {
      display: flex;
      align-items: center;
      gap: var(--space-2);
      margin-block: var(--space-2);
      color: var(--mat-sys-on-surface-variant);
    }
    .note mat-icon {
      flex: none;
      width: 18px;
      height: 18px;
      font-size: 18px;
    }
    .value {
      display: block;
      margin-block: var(--space-3);
      font-size: 1.75rem;
      font-weight: 600;
      letter-spacing: 0.08em;
      text-align: center;
    }
  `,
})
export class NationalIdDialog {
  private readonly http = inject(HttpClient);

  protected readonly data = inject<NationalIdDialogData>(MAT_DIALOG_DATA);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly nationalId = signal<string | null>(null);
  protected readonly reason = new FormControl('', {
    nonNullable: true,
    validators: [Validators.required, Validators.pattern(/\S/)],
  });

  protected async submit(event: Event): Promise<void> {
    event.preventDefault();

    if (this.reason.invalid || this.busy()) {
      this.reason.markAsTouched();
      return;
    }

    this.busy.set(true);
    this.error.set(null);

    try {
      const response = await firstValueFrom(
        this.http.post<{ nationalId: string }>(`/api/v1/users/${this.data.userId}/national-id`, { reason: this.reason.value.trim() }),
      );
      this.nationalId.set(response.nationalId);
    } catch (error) {
      this.error.set(errorKey(error));
    } finally {
      this.busy.set(false);
    }
  }
}
