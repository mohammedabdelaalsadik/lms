import { Component, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { TranslatePipe } from '@ngx-translate/core';
import { ContentSection } from './learning.models';

@Component({
  selector: 'app-section-dialog',
  imports: [MatButtonModule, MatDialogModule, MatFormFieldModule, MatInputModule, ReactiveFormsModule, TranslatePipe],
  template: `
    <h2 mat-dialog-title>{{ (section ? 'content.renameSection' : 'content.addSection') | translate }}</h2>
    <mat-dialog-content>
      <form id="section-form" [formGroup]="form" (ngSubmit)="save()">
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
      </form>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button matButton mat-dialog-close>{{ 'common.cancel' | translate }}</button>
      <button matButton="filled" type="submit" form="section-form">{{ 'common.save' | translate }}</button>
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
export class SectionDialog {
  private readonly dialog = inject(MatDialogRef<SectionDialog>);

  protected readonly section = inject<ContentSection | null>(MAT_DIALOG_DATA);
  protected readonly form = inject(FormBuilder).nonNullable.group({
    titleAr: [this.section?.title.ar ?? '', [Validators.required, Validators.maxLength(200)]],
    titleEn: [this.section?.title.en ?? '', [Validators.required, Validators.maxLength(200)]],
  });

  protected save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.dialog.close(this.form.getRawValue());
  }
}
