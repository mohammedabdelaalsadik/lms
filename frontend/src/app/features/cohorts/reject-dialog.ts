import { Component, inject } from '@angular/core';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { TranslatePipe } from '@ngx-translate/core';

@Component({
  selector: 'app-reject-dialog',
  imports: [
    MatButtonModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    ReactiveFormsModule,
    TranslatePipe,
  ],
  template: `
    <h2 mat-dialog-title>{{ 'roster.rejectTitle' | translate }}</h2>
    <mat-dialog-content>
      <form id="reject-form" (submit)="submit($event)">
        <mat-form-field class="field">
          <mat-label>{{ 'roster.reason' | translate }}</mat-label>
          <textarea matInput rows="3" maxlength="500" [formControl]="reason"></textarea>
          <mat-error>{{ 'validation.required' | translate }}</mat-error>
        </mat-form-field>
      </form>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button matButton mat-dialog-close>{{ 'common.cancel' | translate }}</button>
      <button matButton="filled" type="submit" form="reject-form">
        {{ 'roster.reject' | translate }}
      </button>
    </mat-dialog-actions>
  `,
  styles: `
    form {
      padding-block-start: var(--space-2);
    }
    .field {
      width: 100%;
    }
  `,
})
export class RejectDialog {
  private readonly dialog = inject(MatDialogRef<RejectDialog, string>);

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
