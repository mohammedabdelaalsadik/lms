import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { TranslatePipe } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';
import { errorKey } from '../../core/http/api-error';
import { DateField } from '../../shared/date-field';
import { AssessmentService } from './assessment.service';

@Component({
  selector: 'app-assignment-dialog',
  imports: [DateField, MatButtonModule, MatDialogModule, MatFormFieldModule, MatIconModule, MatInputModule, ReactiveFormsModule, TranslatePipe],
  template: `
    <h2 mat-dialog-title>{{ 'assessments.newAssignment' | translate }}</h2>
    <mat-dialog-content>
      <form id="assignment-form" [formGroup]="form" (ngSubmit)="save()">
        @if (error(); as key) {
          <div class="form-error" role="alert"><mat-icon aria-hidden="true">error</mat-icon>{{ key | translate }}</div>
        }
        <mat-form-field>
          <mat-label>{{ 'content.titleAr' | translate }}</mat-label>
          <input matInput dir="rtl" formControlName="titleAr" />
          <mat-error>{{ 'validation.required' | translate }}</mat-error>
        </mat-form-field>
        <mat-form-field>
          <mat-label>{{ 'content.titleEn' | translate }}</mat-label>
          <input matInput dir="ltr" formControlName="titleEn" />
          <mat-error>{{ 'validation.required' | translate }}</mat-error>
        </mat-form-field>
        <mat-form-field>
          <mat-label>{{ 'assessments.instructionsAr' | translate }}</mat-label>
          <textarea matInput dir="rtl" rows="3" formControlName="instructionsAr"></textarea>
          <mat-error>{{ 'validation.required' | translate }}</mat-error>
        </mat-form-field>
        <mat-form-field>
          <mat-label>{{ 'assessments.instructionsEn' | translate }}</mat-label>
          <textarea matInput dir="ltr" rows="3" formControlName="instructionsEn"></textarea>
          <mat-error>{{ 'validation.required' | translate }}</mat-error>
        </mat-form-field>
        <div class="two-up">
          <app-date-field formControlName="dueAt" [time]="true" [label]="'assessments.dueAt' | translate">{{ 'validation.required' | translate }}</app-date-field>
          <mat-form-field>
            <mat-label>{{ 'assessments.weight' | translate }}</mat-label>
            <input matInput type="number" min="1" formControlName="weight" />
            <mat-error>{{ 'validation.positive' | translate }}</mat-error>
          </mat-form-field>
        </div>
      </form>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button matButton mat-dialog-close>{{ 'common.cancel' | translate }}</button>
      <button matButton="filled" type="submit" form="assignment-form" [disabled]="busy()">{{ 'common.save' | translate }}</button>
    </mat-dialog-actions>
  `,
  styles: `
    form {
      display: grid;
      gap: var(--space-4);
      padding-block-start: var(--space-2);
    }
    .two-up {
      display: grid;
      gap: var(--space-4);
    }
  `,
})
export class AssignmentDialog {
  private readonly assessment = inject(AssessmentService);
  private readonly dialog = inject(MatDialogRef<AssignmentDialog, boolean>);
  private readonly cohortId = inject<string>(MAT_DIALOG_DATA);

  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly form = inject(FormBuilder).nonNullable.group({
    titleAr: ['', [Validators.required, Validators.maxLength(200)]],
    titleEn: ['', [Validators.required, Validators.maxLength(200)]],
    instructionsAr: ['', [Validators.required, Validators.maxLength(4000)]],
    instructionsEn: ['', [Validators.required, Validators.maxLength(4000)]],
    dueAt: ['', Validators.required],
    weight: [10, [Validators.required, Validators.min(1)]],
  });

  protected async save(): Promise<void> {
    if (this.form.invalid || this.busy()) {
      this.form.markAllAsTouched();
      return;
    }

    this.busy.set(true);
    this.error.set(null);

    try {
      const value = this.form.getRawValue();
      await firstValueFrom(
        this.assessment.createAssignment(this.cohortId, { ...value, dueAt: new Date(value.dueAt).toISOString() }),
      );
      this.dialog.close(true);
    } catch (error) {
      this.error.set(errorKey(error));
    } finally {
      this.busy.set(false);
    }
  }
}
