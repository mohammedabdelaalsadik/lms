import { Component, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { TranslatePipe } from '@ngx-translate/core';

export interface GradeDialogData {
  titleKey: string;
  textLabelKey: string;
  textRequired: boolean;
  percent: number | null;
  text: string | null;
}

export interface GradeDialogResult {
  percent: number;
  text: string;
}

/** A grade plus a note: feedback when reviewing work, the reason when requesting a correction. */
@Component({
  selector: 'app-grade-dialog',
  imports: [MatButtonModule, MatDialogModule, MatFormFieldModule, MatInputModule, ReactiveFormsModule, TranslatePipe],
  template: `
    <h2 mat-dialog-title>{{ data.titleKey | translate }}</h2>
    <mat-dialog-content>
      <form id="grade-form" [formGroup]="form" (ngSubmit)="save()">
        <mat-form-field>
          <mat-label>{{ 'grades.percent' | translate }}</mat-label>
          <input matInput type="number" min="0" max="100" step="0.01" formControlName="percent" />
          <mat-error>{{ 'errors.assessment_invalid_grade' | translate }}</mat-error>
        </mat-form-field>
        <mat-form-field>
          <mat-label>{{ data.textLabelKey | translate }}</mat-label>
          <textarea matInput rows="3" maxlength="500" formControlName="text"></textarea>
          <mat-error>{{ 'validation.required' | translate }}</mat-error>
        </mat-form-field>
      </form>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button matButton mat-dialog-close>{{ 'common.cancel' | translate }}</button>
      <button matButton="filled" type="submit" form="grade-form">{{ 'common.save' | translate }}</button>
    </mat-dialog-actions>
  `,
  styles: `
    form {
      display: grid;
      gap: var(--space-4);
      padding-block-start: var(--space-2);
    }
  `,
})
export class GradeDialog {
  private readonly dialog = inject(MatDialogRef<GradeDialog, GradeDialogResult>);

  protected readonly data = inject<GradeDialogData>(MAT_DIALOG_DATA);
  protected readonly form = inject(FormBuilder).nonNullable.group({
    percent: [this.data.percent ?? (null as unknown as number), [Validators.required, Validators.min(0), Validators.max(100)]],
    text: [this.data.text ?? '', this.data.textRequired ? [Validators.required, Validators.pattern(/\S/)] : []],
  });

  protected save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.dialog.close(this.form.getRawValue());
  }
}
