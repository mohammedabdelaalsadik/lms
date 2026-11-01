import { httpResource } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { AbstractControl, FormBuilder, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { TranslatePipe } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';
import { errorKey } from '../../core/http/api-error';
import { DateField } from '../../shared/date-field';
import { Course, Paged, UserOption } from '../../core/models';
import { LocalizedPipe } from '../../shared/localized.pipe';
import { CohortService } from './cohort.service';

function datesInOrder(group: AbstractControl): ValidationErrors | null {
  const { startsOn, endsOn } = group.value as { startsOn: string; endsOn: string };
  return startsOn && endsOn && endsOn < startsOn ? { dateOrder: true } : null;
}

@Component({
  selector: 'app-cohort-form-dialog',
  imports: [
    DateField,
    LocalizedPipe,
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
  templateUrl: './cohort-form-dialog.html',
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
    .hint {
      color: var(--mat-sys-on-surface-variant);
    }
    .date-error {
      color: var(--mat-sys-error);
      font: var(--mat-sys-body-small);
    }
  `,
})
export class CohortFormDialog {
  private readonly cohorts = inject(CohortService);
  private readonly dialog = inject(MatDialogRef<CohortFormDialog, boolean>);

  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  private readonly courseResource = httpResource<Paged<Course>>(() => ({
    url: '/api/v1/courses',
    params: { status: 'Published', pageSize: 100 },
  }));
  private readonly instructorResource = httpResource<UserOption[]>(() => '/api/v1/users/instructors');

  protected readonly courses = computed(() =>
    this.courseResource.hasValue() ? this.courseResource.value().items : [],
  );
  protected readonly instructors = computed(() =>
    this.instructorResource.hasValue() ? this.instructorResource.value() : [],
  );
  protected readonly noInstructors = computed(
    () => this.instructorResource.hasValue() && this.instructors().length === 0,
  );

  protected readonly form = inject(FormBuilder).nonNullable.group(
    {
      courseId: ['', Validators.required],
      nameAr: ['', [Validators.required, Validators.maxLength(150)]],
      nameEn: ['', [Validators.required, Validators.maxLength(150)]],
      branch: ['', [Validators.required, Validators.maxLength(150)]],
      instructorId: ['', Validators.required],
      startsOn: ['', Validators.required],
      endsOn: ['', Validators.required],
      capacity: [20, [Validators.required, Validators.min(1)]],
      requiresApproval: [false],
    },
    { validators: datesInOrder },
  );

  protected async save(): Promise<void> {
    if (this.form.invalid || this.busy()) {
      this.form.markAllAsTouched();
      return;
    }

    this.busy.set(true);
    this.error.set(null);

    try {
      await firstValueFrom(this.cohorts.create(this.form.getRawValue()));
      this.dialog.close(true);
    } catch (error) {
      this.error.set(errorKey(error));
    } finally {
      this.busy.set(false);
    }
  }
}
