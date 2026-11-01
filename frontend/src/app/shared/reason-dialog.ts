import { Component, inject } from '@angular/core';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { TranslatePipe } from '@ngx-translate/core';

export interface ReasonDialogData {
  titleKey: string;
  labelKey: string;
  messageKey?: string;
  confirmKey: string;
}

/** Asks for a required written reason before a serious action. */
@Component({
  selector: 'app-reason-dialog',
  imports: [MatButtonModule, MatDialogModule, MatFormFieldModule, MatInputModule, ReactiveFormsModule, TranslatePipe],
  template: `
    <h2 mat-dialog-title>{{ data.titleKey | translate }}</h2>
    <mat-dialog-content>
      <form id="reason-form" (submit)="submit($event)">
        @if (data.messageKey) {
          <p>{{ data.messageKey | translate }}</p>
        }
        <mat-form-field class="field">
          <mat-label>{{ data.labelKey | translate }}</mat-label>
          <textarea matInput rows="3" maxlength="500" [formControl]="reason"></textarea>
          <mat-error>{{ 'validation.required' | translate }}</mat-error>
        </mat-form-field>
      </form>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button matButton mat-dialog-close>{{ 'common.cancel' | translate }}</button>
      <button matButton="filled" type="submit" form="reason-form">{{ data.confirmKey | translate }}</button>
    </mat-dialog-actions>
  `,
  styles: `
    form {
      display: grid;
      gap: var(--space-3);
      padding-block-start: var(--space-2);
    }
    .field {
      width: 100%;
    }
  `,
})
export class ReasonDialog {
  private readonly dialog = inject(MatDialogRef<ReasonDialog, string>);

  protected readonly data = inject<ReasonDialogData>(MAT_DIALOG_DATA);
  protected readonly reason = new FormControl('', {
    nonNullable: true,
    validators: [Validators.required, Validators.pattern(/\S/)],
  });

  protected submit(event: Event): void {
    event.preventDefault();

    if (this.reason.invalid) {
      this.reason.markAsTouched();
      return;
    }

    this.dialog.close(this.reason.value.trim());
  }
}
