import { Clipboard } from '@angular/cdk/clipboard';
import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { TranslatePipe } from '@ngx-translate/core';
import { UserSummary } from '../../core/models';

export interface UserProfileChange {
  nameAr: string;
  nameEn: string;
  email: string;
}

@Component({
  selector: 'app-user-edit-dialog',
  imports: [MatButtonModule, MatDialogModule, MatFormFieldModule, MatInputModule, ReactiveFormsModule, TranslatePipe],
  template: `
    <h2 mat-dialog-title>{{ 'users.editTitle' | translate }}</h2>
    <form [formGroup]="form" (ngSubmit)="save()">
      <mat-dialog-content class="fields">
        <mat-form-field>
          <mat-label>{{ 'fields.fullNameAr' | translate }}</mat-label>
          <input matInput dir="rtl" maxlength="150" formControlName="nameAr" />
          <mat-error>{{ 'validation.required' | translate }}</mat-error>
        </mat-form-field>
        <mat-form-field>
          <mat-label>{{ 'fields.fullNameEn' | translate }}</mat-label>
          <input matInput dir="ltr" maxlength="150" formControlName="nameEn" />
          <mat-error>{{ 'validation.required' | translate }}</mat-error>
        </mat-form-field>
        <mat-form-field>
          <mat-label>{{ 'fields.email' | translate }}</mat-label>
          <input matInput dir="ltr" type="email" maxlength="254" formControlName="email" />
          <mat-error>{{ 'validation.email' | translate }}</mat-error>
        </mat-form-field>
      </mat-dialog-content>
      <mat-dialog-actions align="end">
        <button matButton type="button" mat-dialog-close>{{ 'common.cancel' | translate }}</button>
        <button matButton="filled" type="submit">{{ 'common.save' | translate }}</button>
      </mat-dialog-actions>
    </form>
  `,
  styles: `
    .fields {
      display: grid;
      gap: var(--space-1);
    }
  `,
})
export class UserEditDialog {
  private readonly user = inject<UserSummary>(MAT_DIALOG_DATA);
  private readonly ref = inject<MatDialogRef<UserEditDialog, UserProfileChange>>(MatDialogRef);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    nameAr: [this.user.fullName.ar, Validators.required],
    nameEn: [this.user.fullName.en, Validators.required],
    email: [this.user.email, [Validators.required, Validators.email]],
  });

  protected save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.ref.close(this.form.getRawValue());
  }
}

/** The only time this password is ever shown. Closing the dialog loses it. */
@Component({
  selector: 'app-temporary-password-dialog',
  imports: [MatButtonModule, MatDialogModule, MatIconModule, TranslatePipe],
  template: `
    <h2 mat-dialog-title>{{ 'users.passwordTitle' | translate }}</h2>
    <mat-dialog-content class="body">
      <p>{{ 'users.passwordMessage' | translate }}</p>
      <code class="ltr-value">{{ password }}</code>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button matButton type="button" (click)="copy()">
        <mat-icon>{{ copied() ? 'check' : 'content_copy' }}</mat-icon>
        {{ (copied() ? 'users.copied' : 'users.copy') | translate }}
      </button>
      <button matButton="filled" type="button" mat-dialog-close>{{ 'common.close' | translate }}</button>
    </mat-dialog-actions>
  `,
  styles: `
    .body {
      display: grid;
      gap: var(--space-3);
    }
    code {
      padding: var(--space-3);
      border: 1px dashed var(--mat-sys-outline);
      border-radius: var(--radius-s);
      background: var(--mat-sys-surface-container-low);
      font-size: 1.125rem;
      text-align: center;
      user-select: all;
    }
  `,
})
export class TemporaryPasswordDialog {
  private readonly clipboard = inject(Clipboard);

  protected readonly password = inject<string>(MAT_DIALOG_DATA);
  protected readonly copied = signal(false);

  protected copy(): void {
    this.copied.set(this.clipboard.copy(this.password));
  }
}
