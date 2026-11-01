import { httpResource } from '@angular/common/http';
import { Component, computed, inject, input } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar } from '@angular/material/snack-bar';
import { RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';
import { errorKey } from '../../core/http/api-error';
import { AppDatePipe } from '../../shared/app-date.pipe';
import { LocalizedPipe } from '../../shared/localized.pipe';
import { StatePanel } from '../../shared/state-panel';
import { SubmissionEntry, Submissions } from './assessment.models';
import { AssessmentService, staffUrl } from './assessment.service';
import { GradeDialog, GradeDialogResult } from './grade-dialog';

@Component({
  selector: 'app-submissions',
  imports: [AppDatePipe, LocalizedPipe, MatButtonModule, MatIconModule, RouterLink, StatePanel, TranslatePipe],
  template: `
    <div class="page">
      <a matButton [routerLink]="['/cohorts', id(), 'assessments']" class="back">
        <mat-icon class="flip-rtl">arrow_back</mat-icon>
        {{ 'assessments.title' | translate }}
      </a>

      @if (resource.error()) {
        <section class="panel"><app-state-panel kind="error" (retry)="resource.reload()" /></section>
      } @else if (!data()) {
        <section class="panel"><app-state-panel kind="loading" /></section>
      } @else {
        <header class="page-header">
          <div>
            <h1>{{ data()!.assignment.title | localized }}</h1>
            <p>{{ 'assessments.dueOn' | translate: { date: (data()!.assignment.dueAt | appDate) } }}</p>
          </div>
        </header>

        <section class="panel">
          @if (data()!.entries.length === 0) {
            <app-state-panel kind="empty" />
          } @else {
            <ul>
              @for (entry of data()!.entries; track entry.id) {
                <li>
                  <div class="who">
                    {{ entry.traineeName | localized }}
                    <small class="ltr-value">{{ entry.maskedNationalId }}</small>
                    <small>{{ entry.submittedAt | appDate }}
                      @if (entry.isLate) {
                        · <strong class="late">{{ 'assessments.late' | translate }}</strong>
                      }
                    </small>
                    @if (entry.feedback) {
                      <small>{{ entry.feedback }}</small>
                    }
                  </div>
                  <div class="tools">
                    @if (entry.percent !== null) {
                      <strong class="ltr-value">{{ entry.percent }}%</strong>
                    }
                    <button matButton (click)="download(entry)">
                      <mat-icon>download</mat-icon>
                      <span class="ltr-value">{{ entry.fileName }}</span>
                    </button>
                    @if (!data()!.gradesPublished) {
                      <button matButton="filled" (click)="review(entry)">{{ 'assessments.grade' | translate }}</button>
                    }
                  </div>
                </li>
              }
            </ul>
          }
        </section>
      }
    </div>
  `,
  styles: `
    .back {
      align-self: flex-start;
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
    .who {
      display: grid;
      gap: 2px;
    }
    small {
      color: var(--mat-sys-on-surface-variant);
    }
    .late {
      color: var(--mat-sys-error);
    }
    .tools {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--space-2);
    }
  `,
})
export class SubmissionsPage {
  private readonly assessment = inject(AssessmentService);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);
  private readonly translate = inject(TranslateService);

  readonly id = input.required<string>();
  readonly assignmentId = input.required<string>();

  protected readonly resource = httpResource<Submissions>(() => `${staffUrl(this.id())}/assignments/${this.assignmentId()}/submissions`);
  protected readonly data = computed(() => (this.resource.hasValue() ? this.resource.value() : null));

  protected async download(entry: SubmissionEntry): Promise<void> {
    try {
      await this.assessment.downloadSubmission(this.id(), entry.id, entry.fileName);
    } catch (error) {
      this.notify(errorKey(error));
    }
  }

  protected async review(entry: SubmissionEntry): Promise<void> {
    const ref = this.dialog.open(GradeDialog, {
      width: '28rem',
      maxWidth: 'calc(100vw - 2rem)',
      data: { titleKey: 'assessments.grade', textLabelKey: 'assessments.feedback', textRequired: false, percent: entry.percent, text: entry.feedback },
    });
    const result = (await firstValueFrom(ref.afterClosed())) as GradeDialogResult | undefined;

    if (!result) {
      return;
    }

    try {
      await firstValueFrom(this.assessment.review(this.id(), entry.id, result.percent, result.text));
      this.notify('assessments.graded');
      this.resource.reload();
    } catch (error) {
      this.notify(errorKey(error));
    }
  }

  private notify(key: string): void {
    this.snackBar.open(this.translate.instant(key), undefined, { duration: 4000 });
  }
}
