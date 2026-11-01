import { httpResource } from '@angular/common/http';
import { Component, computed, inject, input, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar } from '@angular/material/snack-bar';
import { RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';
import { errorKey } from '../../core/http/api-error';
import { AppDatePipe } from '../../shared/app-date.pipe';
import { LocalizedPipe } from '../../shared/localized.pipe';
import { StatePanel } from '../../shared/state-panel';
import { MyAssessments, MyAssignment } from './assessment.models';
import { AssessmentService, myUrl } from './assessment.service';

const MAX_FILE_BYTES = 50 * 1024 * 1024;

@Component({
  selector: 'app-my-assessments',
  imports: [AppDatePipe, LocalizedPipe, MatButtonModule, MatIconModule, RouterLink, StatePanel, TranslatePipe],
  templateUrl: './my-assessments.html',
  styles: `
    .back {
      align-self: flex-start;
    }
    .section-title {
      padding: var(--space-3) var(--space-4);
      border-block-end: 1px solid var(--line);
      font: var(--mat-sys-title-medium);
      font-weight: 600;
    }
    ul {
      margin: 0;
      padding: 0;
      list-style: none;
    }
    li {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      justify-content: space-between;
      gap: var(--space-3);
      padding: var(--space-3) var(--space-4);
      border-block-end: 1px solid var(--line);
    }
    li > div:first-child {
      flex: 1 1 16rem;
      min-width: 0;
    }
    small {
      display: block;
      color: var(--mat-sys-on-surface-variant);
    }
    .note {
      white-space: pre-wrap;
    }
    .late {
      color: var(--mat-sys-error);
    }
    .totals {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-6);
      padding: var(--space-3) var(--space-4);
    }
  `,
})
export class MyAssessmentsPage {
  private readonly assessment = inject(AssessmentService);
  private readonly snackBar = inject(MatSnackBar);
  private readonly translate = inject(TranslateService);

  readonly cohortId = input.required<string>();

  protected readonly uploading = signal<string | null>(null);
  protected readonly resource = httpResource<MyAssessments>(() => myUrl(this.cohortId()));
  protected readonly data = computed(() => (this.resource.hasValue() ? this.resource.value() : null));

  protected async upload(assignment: MyAssignment, event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';

    if (!file) {
      return;
    }

    if (file.size > MAX_FILE_BYTES) {
      this.notify('errors.common_file_too_large');
      return;
    }

    this.uploading.set(assignment.id);

    try {
      await firstValueFrom(this.assessment.submitAssignment(this.cohortId(), assignment.id, file));
      this.notify('assessments.submitted');
      this.resource.reload();
    } catch (error) {
      this.notify(errorKey(error));
    } finally {
      this.uploading.set(null);
    }
  }

  private notify(key: string): void {
    this.snackBar.open(this.translate.instant(key), undefined, { duration: 4000 });
  }
}
