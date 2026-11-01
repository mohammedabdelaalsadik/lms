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
import { Course } from '../../core/models';
import { CourseService } from './course.service';

@Component({
  selector: 'app-course-form-dialog',
  imports: [
    MatButtonModule,
    MatDialogModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    ReactiveFormsModule,
    TranslatePipe,
  ],
  templateUrl: './course-form-dialog.html',
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
    @media (min-width: 600px) {
      .two-up {
        grid-template-columns: 1fr 1fr;
      }
    }
  `,
})
export class CourseFormDialog {
  private readonly courses = inject(CourseService);
  private readonly dialog = inject(MatDialogRef<CourseFormDialog, boolean>);

  protected readonly course = inject<Course | null>(MAT_DIALOG_DATA);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    code: [
      { value: this.course?.code ?? '', disabled: this.course !== null },
      [Validators.required, Validators.maxLength(20), Validators.pattern(/^[A-Za-z0-9-]+$/)],
    ],
    durationHours: [
      this.course?.durationHours ?? 8,
      [Validators.required, Validators.min(1), Validators.max(10000)],
    ],
    titleAr: [this.course?.title.ar ?? '', [Validators.required, Validators.maxLength(200)]],
    titleEn: [this.course?.title.en ?? '', [Validators.required, Validators.maxLength(200)]],
    descriptionAr: [
      this.course?.description.ar ?? '',
      [Validators.required, Validators.maxLength(2000)],
    ],
    descriptionEn: [
      this.course?.description.en ?? '',
      [Validators.required, Validators.maxLength(2000)],
    ],
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
        this.course
          ? this.courses.update(this.course.id, value)
          : this.courses.create(value),
      );
      this.dialog.close(true);
    } catch (error) {
      this.error.set(errorKey(error));
    } finally {
      this.busy.set(false);
    }
  }
}
