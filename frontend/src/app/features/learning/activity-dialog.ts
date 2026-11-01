import { Component, computed, inject, signal, viewChild } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { TranslatePipe } from '@ngx-translate/core';
import { ActivityInput, ActivityKind, ContentActivity } from './learning.models';

export interface ActivityDialogResult {
  input: ActivityInput;
  file: File | null;
}

const MAX_FILE_BYTES = 50 * 1024 * 1024;
import { FormBuilderField } from './form-builder';
import { StructureField } from './structure-field';

@Component({
  selector: 'app-activity-dialog',
  imports: [FormBuilderField, StructureField, 
    MatButtonModule,
    MatDialogModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatSelectModule,
    MatSlideToggleModule,
    ReactiveFormsModule,
    TranslatePipe,
  ],
  templateUrl: './activity-dialog.html',
  styles: `
    form {
      display: grid;
      gap: var(--space-4);
      padding-block-start: var(--space-2);
    }
    .file-row {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--space-3);
    }
    .picked {
      color: var(--mat-sys-on-surface-variant);
    }
    .file-error {
      color: var(--mat-sys-error);
    }
  `,
})
export class ActivityDialog {
  private readonly dialog = inject(MatDialogRef<ActivityDialog, ActivityDialogResult>);

  protected readonly activity = inject<ContentActivity | null>(MAT_DIALOG_DATA);
  protected readonly kinds: ActivityKind[] = ['Page', 'File', 'Video', 'Scorm', 'H5p', 'Form', 'Book', 'Lesson'];
  protected readonly structure = viewChild(StructureField);
  protected readonly builder = viewChild(FormBuilderField);
  protected readonly file = signal<File | null>(null);
  protected readonly fileError = signal<string | null>(null);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    kind: [{ value: this.activity?.kind ?? ('Page' as ActivityKind), disabled: this.activity !== null }],
    titleAr: [this.activity?.title.ar ?? '', [Validators.required, Validators.maxLength(200)]],
    titleEn: [this.activity?.title.en ?? '', [Validators.required, Validators.maxLength(200)]],
    isRequired: [this.activity?.isRequired ?? true],
    bodyAr: [this.activity?.bodyAr ?? ''],
    bodyEn: [this.activity?.bodyEn ?? ''],
    videoUrl: [this.activity?.videoUrl ?? ''],
  });

  private readonly formValue = toSignal(this.form.controls.kind.valueChanges, {
    initialValue: this.form.controls.kind.value,
  });
  protected readonly kind = computed(() => this.formValue());

  protected onFile(event: Event): void {
    const picked = (event.target as HTMLInputElement).files?.[0] ?? null;
    this.fileError.set(picked && picked.size > MAX_FILE_BYTES ? 'errors.common_file_too_large' : null);
    this.file.set(picked);
  }

  protected save(): void {
    const kind = this.form.controls.kind.value;
    const value = this.form.getRawValue();
    const incomplete =
      (kind === 'Page' && (!value.bodyAr.trim() || !value.bodyEn.trim())) ||
      (kind === 'Video' && !value.videoUrl.trim());
    const missingFile = (kind === 'File' || kind === 'Scorm' || kind === 'H5p') && !this.activity && !this.file();

    const form = kind === 'Form' && !this.activity ? (this.builder()?.value() ?? null) : null;

    if (kind === 'Form' && !this.activity && !form) {
      this.fileError.set('forms.incomplete');
      return;
    }

    if (kind === 'Form') {
      this.fileError.set(null);
    }

    const pages = kind === 'Book' || kind === 'Lesson' ? (this.structure()?.value() ?? null) : null;

    if ((kind === 'Book' || kind === 'Lesson') && !pages) {
      this.fileError.set('structure.incomplete');
      return;
    }

    if (missingFile) {
      this.fileError.set('content.fileRequired');
    }

    if (this.form.invalid || incomplete || missingFile || this.fileError()) {
      this.form.markAllAsTouched();
      this.showMissing(kind);
      return;
    }

    this.dialog.close({
      input: {
        kind,
        titleAr: value.titleAr,
        titleEn: value.titleEn,
        isRequired: value.isRequired,
        bodyAr: kind === 'Page' ? value.bodyAr : null,
        bodyEn: kind === 'Page' ? value.bodyEn : null,
        videoUrl: kind === 'Video' ? value.videoUrl : null,
        form,
        book: kind === 'Book' ? { chapters: pages ?? [] } : null,
        lesson: kind === 'Lesson' ? { pages: pages ?? [] } : null,
      },
      file: this.file(),
    });
  }

  private showMissing(kind: ActivityKind): void {
    const { bodyAr, bodyEn, videoUrl } = this.form.controls;

    if (kind === 'Page') {
      bodyAr.setErrors(bodyAr.value.trim() ? null : { required: true });
      bodyEn.setErrors(bodyEn.value.trim() ? null : { required: true });
    }

    if (kind === 'Video') {
      videoUrl.setErrors(videoUrl.value.trim() ? null : { required: true });
    }
  }
}
